using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Financing
{
    /// <summary>
    /// Phase F (F2): financed acquisition execution — price negotiation with
    /// counter-offers, financing assembly (cash + seller note + lender loan),
    /// real title transfer, occupancy registration, and loud failure with no
    /// partial state when financing falls through. All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseFNpcAcquisitionTests
    {
        private sealed class TestSellerPolicy : INpcPropertySellerPolicy
        {
            public int ReservationBps = 9200; // of asking
            public bool AlwaysWalk;
            public int CounterBps = 9500;
            private bool countered;

            public int ReservationPriceCents(string parcelId, int askingPriceCents)
            {
                return askingPriceCents * ReservationBps / 10000;
            }

            public NpcSellerResponse RespondToOffer(string parcelId, int askingPriceCents,
                int buyerOfferCents, int round, int maxRounds, List<string> diag)
            {
                if (AlwaysWalk)
                    return new NpcSellerResponse { Kind = NpcSellerResponseKind.WalkAway, Reason = "the seller will not sell to this buyer." };
                int reservation = ReservationPriceCents(parcelId, askingPriceCents);
                if (buyerOfferCents >= reservation)
                    return new NpcSellerResponse { Kind = NpcSellerResponseKind.Accept, Reason = "meets the reservation." };
                if (!countered)
                {
                    countered = true;
                    return new NpcSellerResponse
                    {
                        Kind = NpcSellerResponseKind.Counter,
                        CounterPriceCents = askingPriceCents * CounterBps / 10000,
                        Reason = "first and only counter.",
                    };
                }
                return new NpcSellerResponse { Kind = NpcSellerResponseKind.WalkAway, Reason = "the seller will not go lower." };
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge Cash = new CreditCashBridge();
            public TitleAuthority Titles = new TitleAuthority();
            public HousingAuthority Housing = new HousingAuthority();
            public SellerFinanceNegotiationBook Negotiations = new SellerFinanceNegotiationBook();
            public SellerFinanceClosingService ClosingService = new SellerFinanceClosingService();
            public NpcCreditDecisionEngine CreditEngine = new NpcCreditDecisionEngine();
            public NpcPropertyAcquisitionService Acquisition = new NpcPropertyAcquisitionService();
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

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            /// <summary>A titled parcel with a house on it, held by the seller.</summary>
            public string RegisterFarmstead(string parcelId, string sellerName)
            {
                Assert.IsNotNull(Titles.RegisterParcel(parcelId, "farmstead", 40f, sellerName,
                    TitleBasis.Purchase, 0, Diag));
                Building building = Housing.RegisterBuilding("bld-" + parcelId, parcelId, "house", 0,
                    "test farmstead", Diag);
                Assert.IsNotNull(building);
                AccommodationSpace space = Housing.DefineSpace(building.BuildingId, 4, Diag);
                Assert.IsNotNull(space);
                return space.SpaceId;
            }

            public NpcAcquisitionPlan BasePlan(string parcelId, string spaceId, int priceCents)
            {
                return new NpcAcquisitionPlan
                {
                    PlanId = "plan-1",
                    BuyerHouseholdId = 7,
                    BuyerName = "household:7",
                    SellerName = "Abel",
                    ParcelId = parcelId,
                    SpaceId = spaceId,
                    OccupantPersonIds = new List<int> { 101, 102 },
                    AgreedPriceCents = priceCents,
                    CashDownCents = priceCents,
                };
            }
        }

        [Test]
        public void Negotiation_CounterOffers_ReachAgreement()
        {
            var f = new Fixture();
            var policy = new TestSellerPolicy(); // reservation 90%, counters once at 95%

            // Buyer opens at 90000 (100000 - 10% discount); the seller
            // counters at 95000; the buyer accepts the counter.
            NpcPriceNegotiationResult result = f.Acquisition.NegotiatePrice(
                policy, "parcel-1", 100000, 100000, 3, 0, f.Diag);

            Assert.IsTrue(result.Agreed, result.FailureReason);
            Assert.AreEqual(95000, result.AgreedPriceCents);
            Assert.GreaterOrEqual(result.Rounds.Count, 2);
        }

        [Test]
        public void Negotiation_SellerWalksAway_NoDeal()
        {
            var f = new Fixture();
            var policy = new TestSellerPolicy { AlwaysWalk = true };

            NpcPriceNegotiationResult result = f.Acquisition.NegotiatePrice(
                policy, "parcel-1", 100000, 100000, 3, 0, f.Diag);

            Assert.IsFalse(result.Agreed);
            Assert.IsNotEmpty(result.FailureReason);
            Assert.Greater(result.Rounds.Count, 0, "the walk-away is observable in the rounds.");
        }

        [Test]
        public void BuyExisting_SellerFinance_EndToEnd_ConservationHolds()
        {
            var f = new Fixture();
            string spaceId = f.RegisterFarmstead("parcel-1", "Abel");
            HouseholdLedger buyerLedger = f.RegisterHousehold(7, 50000);
            PurseCashStore sellerPurse = f.RegisterPurse("Abel", 10000);

            // The negotiation: inquiry → seller counters → buyer accepts.
            SellerFinanceNegotiation negotiation = f.Negotiations.SendInquiry(
                7, "Abel", "parcel-1", "farmstead on parcel-1", 100000, 0, f.Diag);
            Assert.IsNull(f.Negotiations.RecordSellerCounter(negotiation.NegotiationId,
                20000, 600, 24, 3500, 1, f.Diag));
            Assert.IsNull(f.Negotiations.RecordTermsAccepted(negotiation.NegotiationId, 2, f.Diag));

            NpcAcquisitionPlan plan = f.BasePlan("parcel-1", spaceId, 100000);
            plan.CashDownCents = 20000;
            plan.SellerNegotiationId = negotiation.NegotiationId;

            int buyerBefore = buyerLedger.GetBalanceCents();
            int sellerBefore = sellerPurse.ReadBalanceCents();

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow,
                3, f.Events, f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual("household:7", f.Titles.CurrentHolder("parcel-1"), "title is real and queryable.");
            Assert.AreEqual(2, result.OccupancyIds.Count, "the household moves in as owner-occupiers.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SellerNoteObligationId));
            Assert.IsTrue(result.CashConserved, string.Join("; ", result.ConservationNotes));

            // Conservation, checked: seller cash + note == price.
            Assert.AreEqual(20000, sellerPurse.ReadBalanceCents() - sellerBefore);
            Assert.AreEqual(80000, result.SellerNoteFinancedCents);
            Assert.AreEqual(20000 + 80000, 100000);
            // Buyer obligations == financed amount.
            FinancialObligation note = f.Authority.Find(result.SellerNoteObligationId);
            Assert.IsNotNull(note);
            Assert.AreEqual(80000, note.TotalOutstandingCents);
            Assert.AreEqual(buyerBefore - 20000, buyerLedger.GetBalanceCents());
            // The buyer holds no title encumbrance beyond the seller's security interest, not ownership.
            Assert.AreEqual(FinancialObligationKind.SellerFinance, note.Kind);
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void BuyExisting_LenderLoan_EndToEnd()
        {
            var f = new Fixture();
            string spaceId = f.RegisterFarmstead("parcel-1", "Abel");
            HouseholdLedger buyerLedger = f.RegisterHousehold(7, 50000);
            PurseCashStore sellerPurse = f.RegisterPurse("Abel", 0);
            PurseCashStore lenderPurse = f.RegisterPurse("Banker", 100000);
            var funds = new PrivateLenderFunds("Banker", 100000);

            var borrower = new NpcBorrowerProfile
            {
                Name = "household:7", DesiredAmountCents = 60000, MinimumAmountCents = 30000,
                MaxRateBps = 1200, Purpose = "house purchase", DesiredTermDays = 360,
            };
            var creditor = new NpcCreditorProfile
            {
                Name = "Banker",
                PrivateLender = new PrivateLenderCreditParticipant("Banker", funds, lenderPurse),
                Policy = CreditLenderPolicy.ForPrivateIndividual(),
            };
            CreditRequest request = f.CreditEngine.SubmitBorrowerRequest(f.Ids, f.Workflow,
                f.Authority, borrower, creditor, 0, f.Events, f.Diag);
            Assert.IsNotNull(request);
            CreditOffer offer = f.CreditEngine.CreditorEvaluate(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, request, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer, string.Join("; ", f.Diag));
            Assert.AreEqual(CreditOfferStatus.Proposed, offer.Status);

            NpcAcquisitionPlan plan = f.BasePlan("parcel-1", spaceId, 100000);
            plan.CashDownCents = 40000;
            plan.BorrowerProfile = borrower;
            plan.LenderProfile = creditor;
            plan.LenderOffer = offer;
            plan.LenderLoanCents = 60000;

            int buyerBefore = buyerLedger.GetBalanceCents();
            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow,
                3, f.Events, f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason + " | " + string.Join("; ", f.Diag));
            Assert.AreEqual("household:7", f.Titles.CurrentHolder("parcel-1"));
            Assert.IsTrue(result.CashConserved, string.Join("; ", result.ConservationNotes));
            // Buyer: 50000 + 60000 loan - 100000 price = 10000 left.
            Assert.AreEqual(buyerBefore + 60000 - 100000, buyerLedger.GetBalanceCents());
            Assert.AreEqual(100000, sellerPurse.ReadBalanceCents());
            // Buyer obligations == financed amount (60000).
            FinancialObligation loan = f.Authority.Find(result.LenderObligationId);
            Assert.IsNotNull(loan);
            Assert.AreEqual(60000, loan.TotalOutstandingCents);
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void FinancingFallsThrough_FailsLoudly_NoPartialState()
        {
            var f = new Fixture();
            string spaceId = f.RegisterFarmstead("parcel-1", "Abel");
            HouseholdLedger buyerLedger = f.RegisterHousehold(7, 50000);
            PurseCashStore sellerPurse = f.RegisterPurse("Abel", 0);
            // The lender committed 100000 of capital but their purse is
            // spent down elsewhere before closing — the loan cannot advance.
            PurseCashStore lenderPurse = f.RegisterPurse("Banker", 100000);
            var funds = new PrivateLenderFunds("Banker", 100000);

            var borrower = new NpcBorrowerProfile
            {
                Name = "household:7", DesiredAmountCents = 60000, MinimumAmountCents = 30000,
                MaxRateBps = 1200, Purpose = "house purchase", DesiredTermDays = 360,
            };
            var creditor = new NpcCreditorProfile
            {
                Name = "Banker",
                PrivateLender = new PrivateLenderCreditParticipant("Banker", funds, lenderPurse),
                Policy = CreditLenderPolicy.ForPrivateIndividual(),
            };
            CreditRequest request = f.CreditEngine.SubmitBorrowerRequest(f.Ids, f.Workflow,
                f.Authority, borrower, creditor, 0, f.Events, f.Diag);
            CreditOffer offer = f.CreditEngine.CreditorEvaluate(f.Ids, f.Workflow, f.Authority,
                borrower, creditor, request, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual(60000, offer.OfferedAmountCents);

            // Between evaluation and closing, the lender's real cash moves
            // elsewhere — a real event the plan cannot see coming.
            Assert.IsNull(lenderPurse.DebitCents(1, 90000, "other investment", "someone else"));

            NpcAcquisitionPlan plan = f.BasePlan("parcel-1", spaceId, 100000);
            plan.CashDownCents = 40000;
            plan.BorrowerProfile = borrower;
            plan.LenderProfile = creditor;
            plan.LenderOffer = offer;
            plan.LenderLoanCents = 60000;

            int buyerBefore = buyerLedger.GetBalanceCents();
            int sellerBefore = sellerPurse.ReadBalanceCents();
            int obligationCountBefore = new List<FinancialObligation>(f.Authority.Obligations).Count;

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow,
                3, f.Events, f.Diag);

            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.FinancingFellThrough, result.FailureKind);
            Assert.IsNotEmpty(result.FailureReason);
            // No partial state: no title moved, no obligations created, no cash moved.
            Assert.AreEqual("Abel", f.Titles.CurrentHolder("parcel-1"));
            Assert.AreEqual(buyerBefore, buyerLedger.GetBalanceCents());
            Assert.AreEqual(sellerBefore, sellerPurse.ReadBalanceCents());
            Assert.AreEqual(obligationCountBefore, new List<FinancialObligation>(f.Authority.Obligations).Count);
            Assert.AreEqual(0, result.OccupancyIds.Count);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.DecisionFailed).Count > 0,
                "the loud failure is in the event log.");
        }

        [Test]
        public void InvalidPlan_SellerIsNotTheHolder_RefusedBeforeAnythingMoves()
        {
            var f = new Fixture();
            string spaceId = f.RegisterFarmstead("parcel-1", "Abel");
            HouseholdLedger buyerLedger = f.RegisterHousehold(7, 100000);
            f.RegisterPurse("Abel", 0);
            f.RegisterPurse("Mallory", 0);

            NpcAcquisitionPlan plan = f.BasePlan("parcel-1", spaceId, 100000);
            plan.SellerName = "Mallory"; // not the title holder

            int buyerBefore = buyerLedger.GetBalanceCents();
            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow,
                3, f.Events, f.Diag);

            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.InvalidPlan, result.FailureKind);
            Assert.AreEqual("Abel", f.Titles.CurrentHolder("parcel-1"));
            Assert.AreEqual(buyerBefore, buyerLedger.GetBalanceCents());
        }

        [Test]
        public void PureCashPurchase_Closes_TitleMoves()
        {
            var f = new Fixture();
            string spaceId = f.RegisterFarmstead("parcel-1", "Abel");
            HouseholdLedger buyerLedger = f.RegisterHousehold(7, 120000);
            PurseCashStore sellerPurse = f.RegisterPurse("Abel", 0);

            NpcAcquisitionPlan plan = f.BasePlan("parcel-1", spaceId, 100000);
            // CashDownCents = 100000 (full price), no financing legs.

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow,
                3, f.Events, f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual("household:7", f.Titles.CurrentHolder("parcel-1"));
            Assert.AreEqual(100000, sellerPurse.ReadBalanceCents());
            Assert.AreEqual(20000, buyerLedger.GetBalanceCents());
            Assert.IsTrue(result.CashConserved);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SaleAgreementId));
            Assert.AreEqual(0, result.SellerNoteFinancedCents);
            Assert.AreEqual(0, result.LenderLoanCents);
        }
    }
}
