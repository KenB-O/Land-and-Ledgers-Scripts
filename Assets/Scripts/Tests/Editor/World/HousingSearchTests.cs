using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Phase D (Real People): autonomous housing search. A household lacking
    /// appropriate accommodation searches the SAME information the player
    /// uses. Affordability and opportunity determine the path. A house is
    /// never generated from a need; low-income households are never forced
    /// into ownership.
    /// </summary>
    [TestFixture]
    public sealed class HousingSearchTests
    {
        private sealed class FakeBoarderPort : IBoarderCheckInPort
        {
            public readonly List<int> CheckedIn = new List<int>();
            public string CheckIn(int personId, int dayIndex, List<string> diagnostics)
            {
                CheckedIn.Add(personId);
                return null;
            }
        }

        private HousingSearchOption Rental(string id, string spaceId, int places, int upfront, int monthly)
        {
            return new HousingSearchOption
            {
                OptionId = id, PathKind = HousingSearchPathKind.Rental, ProviderName = "Landlord Ames",
                Description = "upper rooms over the harness shop", SpaceId = spaceId, BuildingId = "b-1",
                Arrangement = AccommodationArrangement.Rental, PlacesProvided = places,
                UpfrontCostCents = upfront, RecurringMonthlyCents = monthly,
            };
        }

        private HousingSearchOption Purchase(string id, int price)
        {
            return new HousingSearchOption
            {
                OptionId = id, PathKind = HousingSearchPathKind.Purchase, ProviderName = "Seller Brandt",
                Description = "small house, lot 9", SpaceId = "space-lot9", BuildingId = "b-lot9",
                Arrangement = AccommodationArrangement.OwnerOccupied, PlacesProvided = 4,
                TotalPriceCents = price, UpfrontCostCents = price,
            };
        }

        private List<PersonState> Family(int householdId)
        {
            return new List<PersonState>
            {
                new PersonState { id = 1, age = 34, householdId = householdId, deathDayIndex = -1 },
                new PersonState { id = 2, age = 31, householdId = householdId, deathDayIndex = -1 },
                new PersonState { id = 3, age = 9, householdId = householdId, deathDayIndex = -1 },
            };
        }

        private HousingAuthority HousingWithSpace(string spaceCapacity3)
        {
            var housing = new HousingAuthority();
            var diag = new List<string>();
            housing.RegisterBuilding("b-1", "p-1", "house", 0, "test fixture", diag);
            return housing;
        }

        [Test]
        public void RentalSearchToAgreementToOccupancy()
        {
            var housing = HousingWithSpace("3");
            var diag = new List<string>();
            AccommodationSpace space = housing.DefineSpace("b-1", 3, diag);

            var source = new HousingOpportunityDirectory();
            source.AddRental(Rental("r-1", space.SpaceId, 3, 200, 800));
            var needs = new HousingSearchNeedRegistry();
            HousingSearchNeed need = needs.OpenNeed(1, 3, ResidentialConditionKind.Unsheltered, "test", 10);
            var planner = new HousingSearchPlanner();
            var composition = new HouseholdCompositionSummary { HouseholdId = 1, AdultCount = 2, ChildCount = 1 };

            HousingSearchDecision decision = planner.Plan(need, composition, 5000, 4000, source, 10, diag);
            Assert.IsTrue(decision.Resolved);
            Assert.AreEqual(HousingSearchPathKind.Rental, decision.ChosenOption.PathKind);

            var executor = new HousingSearchExecutor();
            HousingSearchExecutor.ExecutionResult result = executor.Execute(
                decision, Family(1), housing, new FakeBoarderPort(), new SellerFinanceNegotiationBook(),
                new LenderApproachService(), new EntityIdRegistry(), new CreditOfferWorkflow(), 10, diag);

            Assert.IsTrue(result.Executed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.AgreementId));
            Assert.AreEqual(3, result.OccupancyIds.Count);
            Assert.AreEqual(3, housing.CurrentOccupanciesForPerson(1).Count + housing.CurrentOccupanciesForPerson(2).Count + housing.CurrentOccupanciesForPerson(3).Count);
            Assert.AreEqual(AccommodationArrangement.Rental, housing.CurrentOccupanciesForPerson(1)[0].Arrangement);
        }

        [Test]
        public void LowIncomeHouseholdIsNeverForcedIntoOwnership()
        {
            var housing = HousingWithSpace("3");
            var diag = new List<string>();
            AccommodationSpace space = housing.DefineSpace("b-1", 3, diag);

            var source = new HousingOpportunityDirectory();
            source.AddRental(Rental("r-1", space.SpaceId, 3, 200, 800));
            source.AddPropertyForSale(Purchase("p-1", 50000)); // far beyond reach
            var planner = new HousingSearchPlanner();
            var need = new HousingSearchNeed { NeedId = "hsn-0", HouseholdId = 1, RequiredSleepingPlaces = 3 };
            var composition = new HouseholdCompositionSummary { HouseholdId = 1, AdultCount = 2, ChildCount = 1 };

            // 500c cash: cannot buy, can rent.
            HousingSearchDecision decision = planner.Plan(need, composition, 500, 4000, source, 10, diag);

            Assert.IsTrue(decision.Resolved);
            Assert.AreEqual(HousingSearchPathKind.Rental, decision.ChosenOption.PathKind);
            Assert.AreEqual(1, decision.RejectedAlternatives.Count);
            Assert.AreEqual(HousingSearchPathKind.Purchase, decision.RejectedAlternatives[0].PathKind);
            StringAssert.Contains("not affordable", decision.RejectedAlternatives[0].Reason);
        }

        [Test]
        public void UnsuitableOptionsAreRejectedWithReasons()
        {
            var housing = HousingWithSpace("3");
            var diag = new List<string>();
            AccommodationSpace space = housing.DefineSpace("b-1", 1, diag); // one bunk only

            var source = new HousingOpportunityDirectory();
            source.AddRental(Rental("r-1", space.SpaceId, 1, 200, 800)); // fits nobody in a family of 3
            var planner = new HousingSearchPlanner();
            var need = new HousingSearchNeed { NeedId = "hsn-0", HouseholdId = 1, RequiredSleepingPlaces = 3 };
            var composition = new HouseholdCompositionSummary { HouseholdId = 1, AdultCount = 2, ChildCount = 1 };

            HousingSearchDecision decision = planner.Plan(need, composition, 50000, 40000, source, 10, diag);

            Assert.IsFalse(decision.Resolved);
            Assert.IsNull(decision.ChosenOption);
            Assert.AreEqual(1, decision.RejectedAlternatives.Count);
            StringAssert.Contains("unsuitable", decision.RejectedAlternatives[0].Reason);
        }

        [Test]
        public void AffordablePurchaseBecomesDelegatedStepNotGeneratedHouse()
        {
            var housing = HousingWithSpace("3");
            var diag = new List<string>();
            var source = new HousingOpportunityDirectory();
            source.AddPropertyForSale(Purchase("p-1", 5000));
            var planner = new HousingSearchPlanner();
            var need = new HousingSearchNeed { NeedId = "hsn-0", HouseholdId = 1, RequiredSleepingPlaces = 3 };
            var composition = new HouseholdCompositionSummary { HouseholdId = 1, AdultCount = 2, ChildCount = 1 };

            // Wealthy household: purchase IS affordable — and still not generated.
            HousingSearchDecision decision = planner.Plan(need, composition, 50000, 40000, source, 10, diag);
            Assert.IsTrue(decision.Resolved);
            Assert.AreEqual(HousingSearchPathKind.Purchase, decision.ChosenOption.PathKind);

            var executor = new HousingSearchExecutor();
            HousingSearchExecutor.ExecutionResult result = executor.Execute(
                decision, Family(1), housing, new FakeBoarderPort(), new SellerFinanceNegotiationBook(),
                new LenderApproachService(), new EntityIdRegistry(), new CreditOfferWorkflow(), 10, diag);

            Assert.IsTrue(result.Executed);
            Assert.AreEqual(0, result.OccupancyIds.Count); // no occupancy invented
            Assert.AreEqual(1, result.DelegatedSteps.Count);
            Assert.AreEqual(HousingSearchPathKind.Purchase, result.DelegatedSteps[0].PathKind);
        }

        [Test]
        public void SellerFinanceNegotiationInquiryToAcceptance()
        {
            var book = new SellerFinanceNegotiationBook();
            var diag = new List<string>();

            SellerFinanceNegotiation negotiation = book.SendInquiry(1, "Seller Brandt", "parcel-9",
                "lot 9 with small house", 5000, 10, diag);
            Assert.AreEqual(SellerFinanceNegotiationStatus.InquirySent, negotiation.Status);

            Assert.IsNull(book.RecordSellerCounter(negotiation.NegotiationId, 1000, 600, 60, 80, 11, diag));
            Assert.AreEqual(SellerFinanceNegotiationStatus.SellerCountered, negotiation.Status);
            Assert.AreEqual(1000, negotiation.ProposedDownPaymentCents);

            Assert.IsNull(book.RecordTermsAccepted(negotiation.NegotiationId, 12, diag));
            Assert.AreEqual(SellerFinanceNegotiationStatus.TermsAccepted, negotiation.Status);
            Assert.AreEqual(3, negotiation.History.Count);

            var dto = book.CaptureSaveDto();
            var reloaded = new SellerFinanceNegotiationBook();
            reloaded.LoadFromSaveDto(dto);
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(SellerFinanceNegotiationStatus.TermsAccepted,
                reloaded.Find(negotiation.NegotiationId).Status);
        }

        [Test]
        public void LenderApproachSubmitsRealCreditRequest()
        {
            var service = new LenderApproachService();
            var workflow = new CreditOfferWorkflow();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();

            CreditRequest request = service.SubmitApproach(ids, workflow, 1, "Black Hills Bank",
                5000, "housing finance", 10, diag);

            Assert.IsNotNull(request);
            Assert.AreEqual("household:1", request.Borrower);
            Assert.AreEqual("Black Hills Bank", request.Lender);
            Assert.AreEqual(request, workflow.FindRequest(request.RequestId));
        }

        [Test]
        public void BoardingExecutesThroughThePort()
        {
            var housing = new HousingAuthority();
            var diag = new List<string>();
            var source = new HousingOpportunityDirectory();
            source.AddBoarding(new HousingSearchOption
            {
                OptionId = "bd-1", PathKind = HousingSearchPathKind.Boarding,
                ProviderName = "Widow Hart's Boarding House", Description = "two bunks",
                PlacesProvided = 2, UpfrontCostCents = 100, RecurringMonthlyCents = 1200,
            });
            var planner = new HousingSearchPlanner();
            var need = new HousingSearchNeed { NeedId = "hsn-0", HouseholdId = 2, RequiredSleepingPlaces = 1 };
            var composition = new HouseholdCompositionSummary { HouseholdId = 2, AdultCount = 1 };
            HousingSearchDecision decision = planner.Plan(need, composition, 5000, 4000, source, 10, diag);
            Assert.IsTrue(decision.Resolved);

            var port = new FakeBoarderPort();
            var executor = new HousingSearchExecutor();
            var members = new List<PersonState>
            {
                new PersonState { id = 11, age = 26, householdId = 2, deathDayIndex = -1 },
            };
            HousingSearchExecutor.ExecutionResult result = executor.Execute(
                decision, members, housing, port, new SellerFinanceNegotiationBook(),
                new LenderApproachService(), new EntityIdRegistry(), new CreditOfferWorkflow(), 10, diag);

            Assert.IsTrue(result.Executed);
            Assert.AreEqual(1, port.CheckedIn.Count);
            Assert.AreEqual(11, port.CheckedIn[0]);
        }
    }
}
