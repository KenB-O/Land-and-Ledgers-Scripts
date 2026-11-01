using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Financing
{
    /// <summary>
    /// Phase E (E4): NPC credit decisions executing through the real systems —
    /// request → evidence → evaluation → negotiation → acceptance → conserved
    /// cash → obligation → repayment → missed payment → workout/enforcement,
    /// with rent arrears able to flow into the same machinery. All run in the
    /// Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseENpcCreditLoopTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge Cash = new CreditCashBridge();
            public CreditWorkoutService Workout = new CreditWorkoutService();
            public CreditEventLog Events = new CreditEventLog();
            public TitleAuthority Titles = new TitleAuthority();
            public List<string> Diag = new List<string>();
            public NpcCreditDecisionEngine Engine = new NpcCreditDecisionEngine();

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

            public NpcBorrowerProfile Borrower(string name, int desired, int maxRateBps)
            {
                return new NpcBorrowerProfile
                {
                    Name = name, DesiredAmountCents = desired,
                    MinimumAmountCents = desired / 2, MaxRateBps = maxRateBps,
                    Purpose = "seed stock", DesiredTermDays = 360,
                };
            }

            public NpcCreditorProfile PrivateCreditor(string name, int capitalCents)
            {
                var purse = RegisterPurse(name, capitalCents);
                var funds = new PrivateLenderFunds(name, capitalCents);
                return new NpcCreditorProfile
                {
                    Name = name,
                    PrivateLender = new PrivateLenderCreditParticipant(name, funds, purse),
                    Policy = CreditLenderPolicy.ForPrivateIndividual(),
                    RelationshipMatters = true,
                };
            }
        }

        [Test]
        public void FullLoop_PrivateLender_RealCash_RepaidToSatisfaction()
        {
            var f = new Fixture();
            f.RegisterPurse("Baker", 300000);
            NpcBorrowerProfile borrower = f.Borrower("Baker", 100000, 1000);
            NpcCreditorProfile creditor = f.PrivateCreditor("Abel", 200000);

            NpcCreditLoopResult result = f.Engine.RunFullLoop(f.Ids, f.Workflow, f.Authority,
                f.Cash, f.Workout, f.Registry, null, f.Titles,
                borrower, creditor, 0, 400, f.Events, f.Diag);

            Assert.IsTrue(result.RequestFiled);
            Assert.IsTrue(result.OfferMade, string.Join("; ", result.Notes));
            Assert.IsTrue(result.Closed, string.Join("; ", result.Notes));
            Assert.IsTrue(result.CashConserved, "Every cent advanced must come from real cash.");
            Assert.AreEqual(FinancialObligationStatus.Satisfied, result.FinalStatus);
            Assert.AreEqual(0, result.FinalOutstandingCents);
            Assert.AreEqual(0, result.PaymentsMissed);
            Assert.Greater(result.PaymentsCollected, 0);

            // The lender's finite commitment is released on satisfaction.
            Assert.AreEqual(200000, creditor.PrivateLender.Funds.AvailableCents());

            // The event log tells the whole story.
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.NegotiationSubmitted).Count > 0);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.OfferMade).Count > 0);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.Settlement).Count > 0);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.PaymentCollected).Count > 0);
            string reconcileMissed = string.Join("; ", f.Authority.Reconcile());
            Assert.IsEmpty(f.Authority.Reconcile(), "reconcile: " + reconcileMissed);
        }

        [Test]
        public void FullLoop_MissedPayment_TriggersWorkout_PrincipalUntouched()
        {
            var f = new Fixture();
            f.RegisterPurse("Baker", 20000);
            // Two small standing obligations: the honest borrower discloses
            // them, satisfying the bank's stricter evidence standard.
            f.Authority.Create(f.Ids, FinancialObligationKind.TradeCredit,
                "Baker", "General Store", 1200, 0, "net 30", "flour");
            f.Authority.Create(f.Ids, FinancialObligationKind.TradeCredit,
                "Baker", "Blacksmith", 800, 0, "net 30", "horseshoes");
            NpcBorrowerProfile borrower = f.Borrower("Baker", 100000, 1000);
            var vault = f.RegisterPurse("Frontier Bank", 1000000);
            var bank = new BankCreditParticipant("Frontier Bank", vault, 0);
            var creditor = new NpcCreditorProfile
            {
                Name = "Frontier Bank",
                Bank = bank,
                Policy = CreditLenderPolicy.ForFormalBank(),
                RelationshipMatters = false,
            };

            // Drive the loop step by step so the test can spend the proceeds:
            // Baker borrowed for seed stock, and spends it.
            var engine = f.Engine;
            CreditRequest request = engine.SubmitBorrowerRequest(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, 0, f.Events, f.Diag);
            Assert.IsNotNull(request);
            CreditOffer offer = engine.CreditorEvaluate(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, request, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer);
            bool responded = engine.BorrowerRespond(f.Workflow, borrower, creditor, offer, 0, f.Events, f.Diag);
            Assert.IsTrue(responded, "borrower should respond to the offer");
            FinancialObligation obligation = engine.CloseLoan(f.Ids, f.Workflow, f.Authority,
                f.Cash, borrower, creditor, offer, 0, f.Events, f.Diag);
            Assert.IsNotNull(obligation, "closing should produce an obligation");

            CreditCashAccount spend = f.Cash.OpenWindow("Baker", f.Diag);
            spend.BalanceCents = 15000; // the seed stock is bought; little cash remains
            Assert.IsNull(f.Cash.CommitWindow(spend, 1, "seed stock purchases", "General Store", f.Diag), "spend-down commit");

            var result = new NpcCreditLoopResult();
            engine.SimulateRepayment(f.Ids, f.Workflow, f.Authority, f.Cash, f.Workout,
                f.Registry, null, f.Titles, borrower, creditor,
                obligation.ObligationId, offer.OfferId, 1, 400, result, f.Events, f.Diag);

            Assert.Greater(result.PaymentsMissed, 0, "The balloon payment must be missed.");
            Assert.AreEqual(obligation.OriginalPrincipalCents,
                f.Authority.Find(obligation.ObligationId).OutstandingPrincipalCents,
                "Workout never rewrites principal.");

            // The bank does not forbear like a neighbor would: a workout was offered.
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.WorkoutRestructured).Count > 0,
                "Delinquency must reach a real workout response.");
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.PaymentMissed).Count > 0, "missed-payment events");
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.DelinquencyMarked).Count > 0, "delinquency events");
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void FullLoop_GuarantyCall_NoPrincipalDuplication()
        {
            var f = new Fixture();
            PurseCashStore borrowerPurse = f.RegisterPurse("Baker", 300000);
            PurseCashStore guarantorPurse = f.RegisterPurse("Gus", 300000);
            NpcBorrowerProfile borrower = f.Borrower("Baker", 100000, 1000);
            borrower.GuarantorOffered = "Gus";
            NpcCreditorProfile creditor = f.PrivateCreditor("Abel", 200000);

            var engine = f.Engine;
            CreditRequest request = engine.SubmitBorrowerRequest(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, 0, f.Events, f.Diag);
            Assert.IsNotNull(request);
            CreditOffer offer = engine.CreditorEvaluate(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, request, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual("Gus", offer.GuarantorRequired);
            bool responded = engine.BorrowerRespond(f.Workflow, borrower, creditor, offer, 0, f.Events, f.Diag);
            Assert.IsTrue(responded, "borrower should respond to the offer");
            FinancialObligation obligation = engine.CloseLoan(f.Ids, f.Workflow, f.Authority,
                f.Cash, borrower, creditor, offer, 0, f.Events, f.Diag);
            Assert.IsNotNull(obligation, "closing should produce an obligation");
            Assert.AreEqual(1, obligation.GuarantyIds.Count);

            // The borrower defaults outright; the sweep accelerates to the guaranty call.
            f.Authority.MarkDefaulted(obligation.ObligationId);
            f.Workout.SweepDay(f.Ids, f.Authority, f.Registry, f.Cash, null, f.Titles,
                new Dictionary<string, CreditParticipantService>
                {
                    { "Abel", new CreditParticipantService("Abel", CreditParticipantRole.PrivatePerson, 100000, 1f) },
                },
                50, f.Events, f.Diag);

            FinancialObligation covered = f.Authority.Find(obligation.ObligationId);
            GuarantyRecord guaranty = null;
            foreach (GuarantyRecord g in f.Authority.Guaranties)
                if (g.GuarantyId == obligation.GuarantyIds[0]) guaranty = g;
            Assert.IsNotNull(guaranty);
            Assert.IsTrue(guaranty.Called);
            FinancialObligation called = f.Authority.Find(guaranty.CalledObligationId);
            Assert.IsNotNull(called);
            Assert.AreEqual(FinancialObligationKind.GuarantyCall, called.Kind);

            // Gus could pay: his real cash settled the call. The economics:
            // the covered loan is satisfied BY the guarantor (not cloned),
            // the creditor was paid exactly once, and Gus holds a
            // subrogation claim against Baker for the same 100000c.
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.GuarantyCalled).Count > 0);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.GuarantySettled).Count > 0);
            Assert.IsTrue(covered.Settled, "The covered loan is satisfied by the guarantor's payment.");
            Assert.IsTrue(called.Settled, "The called guaranty obligation is satisfied by Gus's payment.");
            Assert.IsNotNull(f.Authority.Find(guaranty.RecoveryObligationId));
            FinancialObligation recovery = f.Authority.Find(guaranty.RecoveryObligationId);
            Assert.AreEqual("Baker", recovery.Debtor);
            Assert.AreEqual("Gus", recovery.Creditor);
            Assert.AreEqual(100000, recovery.OutstandingPrincipalCents);

            int openPrincipal = 0;
            foreach (FinancialObligation o in f.Authority.Obligations)
                if (!o.Settled) openPrincipal += o.OutstandingPrincipalCents;
            Assert.AreEqual(100000, openPrincipal,
                "Only the subrogation claim is still open — no principal was duplicated anywhere.");

            int allCash = borrowerPurse.ReadBalanceCents() + guarantorPurse.ReadBalanceCents()
                + creditor.PrivateLender.Purse.ReadBalanceCents();
            Assert.AreEqual(800000, allCash,
                "Cash conservation across borrower + guarantor + lender: 300000 + 300000 + 200000 in, same out.");
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void RentArrears_RouteIntoWorkoutMachinery()
        {
            var f = new Fixture();
            f.RegisterPurse("household:7", 1000);
            f.RegisterPurse("Boarding House", 50000);

            // Phase D books arrears as a real obligation; here it is already delinquent.
            FinancialObligation arrears = f.Authority.Create(f.Ids, FinancialObligationKind.Payable,
                "household:7", "Boarding House", 2400, 0, "boarding rent arrears", "rent");
            f.Authority.MarkDelinquent(arrears.ObligationId);

            var landlord = new NpcCreditorProfile
            {
                Name = "Boarding House",
                Participant = new CreditParticipantService("Boarding House",
                    CreditParticipantRole.Business, 50000, 1f),
                Policy = CreditLenderPolicy.ForPrivateIndividual(),
                RelationshipMatters = false,
            };
            f.Engine.ProcessRentArrears(f.Ids, f.Authority, f.Registry, f.Cash, f.Workout,
                null, f.Titles, landlord, new[] { arrears.ObligationId }, 30, f.Events, f.Diag);

            // The business does not forbear: delinquent arrears get a workout.
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.WorkoutRestructured).Count > 0,
                "Rent arrears must flow into the same workout machinery.");
            Assert.AreEqual(2400, arrears.OutstandingPrincipalCents);
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void DishonestBorrower_Nondisclosure_LimitsCreditorKnowledge()
        {
            var f = new Fixture();
            f.RegisterPurse("Baker", 300000);
            // Baker secretly owes Carla 200000c — the lender cannot know it.
            f.Authority.Create(f.Ids, FinancialObligationKind.Loan, "Baker", "Carla",
                200000, 0, "terms", "wagon");

            NpcBorrowerProfile borrower = f.Borrower("Baker", 100000, 1000);
            borrower.HonestDisclosure = false;
            NpcCreditorProfile creditor = f.PrivateCreditor("Abel", 200000);

            CreditRequest request = f.Engine.SubmitBorrowerRequest(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, 0, f.Events, f.Diag);
            Assert.IsNotNull(request);
            int disclosed = 0;
            foreach (CreditEvidenceRecord record in f.Workflow.Evidence)
                if (record.DisclosedToLender && record.Borrower == "Baker") disclosed++;
            Assert.AreEqual(0, disclosed,
                "A dishonest borrower discloses nothing — the lender underwrites on thin knowledge, honestly modeled.");
            CreditOffer offer = f.Engine.CreditorEvaluate(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, request, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer, "With no disclosed debt, the private lender still offers — on what they could know.");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesCreditState()
        {
            var f = new Fixture();
            f.RegisterPurse("Baker", 300000);
            NpcBorrowerProfile borrower = f.Borrower("Baker", 100000, 1000);
            NpcCreditorProfile creditor = f.PrivateCreditor("Abel", 200000);
            NpcCreditLoopResult result = f.Engine.RunFullLoop(f.Ids, f.Workflow, f.Authority,
                f.Cash, f.Workout, f.Registry, null, f.Titles,
                borrower, creditor, 0, 400, f.Events, f.Diag);
            Assert.IsTrue(result.Closed);

            var restoredAuthority = new FinancialObligationAuthority();
            restoredAuthority.LoadFromSaveDto(f.Authority.CaptureSaveDto());
            Assert.AreEqual(f.Authority.Obligations.Count, restoredAuthority.Obligations.Count);
            Assert.IsEmpty(restoredAuthority.Reconcile());

            var restoredWorkflow = new CreditOfferWorkflow();
            restoredWorkflow.LoadFromSaveDto(f.Workflow.CaptureSaveDto());
            Assert.AreEqual(f.Workflow.Offers.Count, restoredWorkflow.Offers.Count);
            Assert.AreEqual(f.Workflow.Requests.Count, restoredWorkflow.Requests.Count);

            var restoredEvents = new CreditEventLog();
            restoredEvents.LoadFromSaveDto(f.Events.CaptureSaveDto());
            Assert.AreEqual(f.Events.Count, restoredEvents.Count);

            var book = new TradeCreditBook();
            FinancialObligation invoice = f.Authority.Create(f.Ids, FinancialObligationKind.TradeCredit,
                "household:7", "General Store", 5000, 0, "net 30", "flour");
            book.RegisterTerms(f.Authority, invoice.ObligationId, 30, 0, f.Diag);
            var restoredBook = new TradeCreditBook();
            restoredBook.LoadFromSaveDto(book.CaptureSaveDto());
            Assert.AreEqual(TradeCreditCollectionStage.Current,
                restoredBook.FindTerms(invoice.ObligationId).Stage);
        }
    }
}
