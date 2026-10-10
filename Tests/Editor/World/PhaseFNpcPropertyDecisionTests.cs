using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Phase F (F1): the NPC property decision engine — a household with a
    /// housing need or investment motive evaluates REAL options and decides:
    /// continue searching, rent, buy existing, buy land+build, or walk away.
    /// All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseFNpcPropertyDecisionTests
    {
        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private sealed class Fixture
        {
            public List<string> Diag = new List<string>();
            public HousingSearchNeedRegistry Needs = new HousingSearchNeedRegistry();
            public HousingOpportunityDirectory Source = new HousingOpportunityDirectory();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public FakePricePort Prices = new FakePricePort();
            public NpcPropertyDecisionEngine Engine = new NpcPropertyDecisionEngine();

            public HouseholdLedger LedgerWithCash(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                return ledger;
            }

            public HousingSearchNeed OpenNeed(int householdId, int places)
            {
                return Needs.OpenNeed(householdId, places, ResidentialConditionKind.Unsheltered,
                    "test need", 0);
            }

            public HousingSearchOption PurchaseOption(string id, string desc, int priceCents,
                string spaceId = "space-1", string parcelId = "parcel-1")
            {
                return new HousingSearchOption
                {
                    OptionId = id, PathKind = HousingSearchPathKind.Purchase,
                    ProviderName = "Abel", Description = desc,
                    SpaceId = spaceId, ParcelId = parcelId,
                    PlacesProvided = 4, TotalPriceCents = priceCents,
                };
            }

            public HousingSearchOption RentalOption(string id, string desc, int monthlyCents, int places = 4)
            {
                return new HousingSearchOption
                {
                    OptionId = id, PathKind = HousingSearchPathKind.Rental,
                    ProviderName = "Landlord", Description = desc,
                    SpaceId = "space-r1", PlacesProvided = places,
                    UpfrontCostCents = monthlyCents, RecurringMonthlyCents = monthlyCents,
                };
            }

            public NpcPropertyFinancingAvailability NoFinancing(int incomeCents)
            {
                return new NpcPropertyFinancingAvailability { DocumentedMonthlyIncomeCents = incomeCents };
            }

            public BuildingDesign CabinDesign()
            {
                var design = new BuildingDesign
                {
                    DesignId = "cabin-a", DisplayName = "Settler cabin", KindLabel = "cabin",
                };
                var foundation = new BuildingDesignPhase { PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240 };
                foundation.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-stone", MaterialKind = ConstructionMaterialKind.Other,
                    MaterialDisplayName = "fieldstone", RequiredUnits = 40, UnitLabel = "perch",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                var framing = new BuildingDesignPhase { PhaseName = "Framing", Sequence = 1, LaborMinutes = 480 };
                framing.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Other,
                    MaterialDisplayName = "lumber", RequiredUnits = 600, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                design.Phases.Add(foundation);
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                return design;
            }
        }

        [Test]
        public void OwnershipMotive_WithSellerFinance_BuysExisting()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.LedgerWithCash(7, 20000);
            HousingSearchNeed need = f.OpenNeed(7, 3);
            f.Source.AddPropertyForSale(f.PurchaseOption("opt-1", "farmhouse", 100000));

            var financing = f.NoFinancing(4000);
            financing.HasSellerFinanceTerms = true;
            financing.SellerFinanceNegotiationId = "sfn-0";
            financing.SellerFinanceAssetId = "parcel-1";
            financing.SellerFinancePriceCents = 100000;
            financing.SellerFinanceDownPaymentCents = 20000;
            financing.SellerFinanceMonthlyCents = 800;

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2, ChildCount = 1 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Ownership, f.Source, financing, f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            Assert.AreEqual(NpcPropertyDecisionKind.BuyExistingProperty, decision.Kind);
            Assert.AreEqual(100000, decision.AgreedPriceCents);
            Assert.AreEqual(20000, decision.CashDownCents);
            Assert.AreEqual(80000, decision.FinancedCents);
            Assert.AreEqual(800, decision.EstimatedMonthlyBurdenCents);
            Assert.IsTrue(decision.RejectedAlternatives.Count > 0,
                "the planner's cash rejection of the purchase must be carried over as a rejected alternative.");
            Assert.IsFalse(decision.NeedWithdrawn);
        }

        [Test]
        public void PricedOutHousehold_WalksAway_NeedWithdrawn()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.LedgerWithCash(7, 500);
            HousingSearchNeed need = f.OpenNeed(7, 2);
            f.Source.AddRental(f.RentalOption("rent-1", "room", 5000));
            f.Source.AddPropertyForSale(f.PurchaseOption("opt-1", "farmhouse", 100000));

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Shelter, f.Source, f.NoFinancing(0), f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            Assert.AreEqual(NpcPropertyDecisionKind.WalkAway, decision.Kind);
            Assert.IsTrue(decision.NeedWithdrawn);
            Assert.AreEqual(HousingSearchNeedStatus.Withdrawn, need.Status);
            Assert.IsNull(f.Needs.GetOpenNeed(7));
            Assert.IsTrue(decision.RejectedAlternatives.Count >= 2, "every option rejected with a reason");
            StringAssert.Contains("walks away", decision.Rationale);
        }

        [Test]
        public void ShelterMotive_ChoosesCheapestAdequateRent()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.LedgerWithCash(7, 30000);
            HousingSearchNeed need = f.OpenNeed(7, 2);
            f.Source.AddRental(f.RentalOption("rent-1", "cheap room", 2000));
            f.Source.AddRental(f.RentalOption("rent-2", "dear room", 4000));
            f.Source.AddPropertyForSale(f.PurchaseOption("opt-1", "farmhouse", 100000));

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Shelter, f.Source, f.NoFinancing(3000), f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            Assert.AreEqual(NpcPropertyDecisionKind.Rent, decision.Kind);
            Assert.AreEqual(HousingSearchPathKind.Rental, decision.ChosenPath);
            Assert.AreEqual("rent-1", decision.ChosenOption.OptionId);
        }

        [Test]
        public void LandBuild_ChosenWhenFinancible_DesignSelected()
        {
            var f = new Fixture();
            Assert.IsNull(f.Catalog.RegisterDesign(f.CabinDesign(), f.Diag));
            f.Prices.Prices["fieldstone"] = 50;   // 40 x 50 = 2000c
            f.Prices.Prices["lumber"] = 40;       // 600 x 40 = 24000c → build ~26000c
            HouseholdLedger ledger = f.LedgerWithCash(7, 60000);
            HousingSearchNeed need = f.OpenNeed(7, 3);
            f.Source.AddLandParcel(new HousingSearchOption
            {
                OptionId = "land-1", PathKind = HousingSearchPathKind.LandPurchaseAndBuild,
                ProviderName = "County", Description = "quarter section",
                ParcelId = "parcel-9", PlacesProvided = 0, TotalPriceCents = 15000,
            });

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2, ChildCount = 1 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Ownership, f.Source, f.NoFinancing(5000), f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            Assert.AreEqual(NpcPropertyDecisionKind.BuyLandAndBuild, decision.Kind);
            Assert.AreEqual("cabin-a", decision.ChosenDesignId);
            Assert.AreEqual(26000, decision.EstimatedBuildCostCents);
            Assert.AreEqual(41000, decision.AgreedPriceCents); // land 15000 + build 26000
            Assert.AreEqual(41000, decision.CashDownCents);
            Assert.AreEqual(0, decision.FinancedCents);
        }

        [Test]
        public void LandBuild_RejectedWhenMaterialsUnpriced()
        {
            var f = new Fixture();
            Assert.IsNull(f.Catalog.RegisterDesign(f.CabinDesign(), f.Diag));
            // No prices registered — build cost unknowable.
            HouseholdLedger ledger = f.LedgerWithCash(7, 60000);
            HousingSearchNeed need = f.OpenNeed(7, 3);
            f.Source.AddLandParcel(new HousingSearchOption
            {
                OptionId = "land-1", PathKind = HousingSearchPathKind.LandPurchaseAndBuild,
                ProviderName = "County", Description = "quarter section",
                ParcelId = "parcel-9", PlacesProvided = 0, TotalPriceCents = 15000,
            });

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2, ChildCount = 1 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Ownership, f.Source, f.NoFinancing(5000), f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            // Nothing else is on offer and land+build is undecidable → the
            // market has an option but it cannot be evaluated: keep searching.
            Assert.AreEqual(NpcPropertyDecisionKind.ContinueSearching, decision.Kind);
            bool rejectionRecorded = false;
            foreach (HousingRejectedAlternative rejected in decision.RejectedAlternatives)
                if (rejected.OptionId == "land-1") rejectionRecorded = true;
            Assert.IsTrue(rejectionRecorded, "the land option must be rejected with a reason, not dropped silently.");
        }

        [Test]
        public void RiskCheck_RejectsWhenMonthlyExceedsBurdenShare()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.LedgerWithCash(7, 50000);
            HousingSearchNeed need = f.OpenNeed(7, 2);
            f.Source.AddPropertyForSale(f.PurchaseOption("opt-1", "farmhouse", 100000));

            // Seller terms the household cannot service: 3000c/mo against 4000c income (burden cap 2000c).
            var financing = f.NoFinancing(4000);
            financing.HasSellerFinanceTerms = true;
            financing.SellerFinancePriceCents = 100000;
            financing.SellerFinanceDownPaymentCents = 20000;
            financing.SellerFinanceMonthlyCents = 3000;

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Ownership, f.Source, financing, f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            Assert.AreEqual(NpcPropertyDecisionKind.WalkAway, decision.Kind,
                "the NPC must not take terms it cannot service — it walks away.");
            bool riskRecorded = false;
            foreach (HousingRejectedAlternative rejected in decision.RejectedAlternatives)
                if (rejected.Reason.Contains("2000c/mo")) riskRecorded = true;
            Assert.IsTrue(riskRecorded, "the burden-share rejection must name the numbers.");
        }

        [Test]
        public void LenderLeg_ClosesFinancingGap()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.LedgerWithCash(7, 40000);
            HousingSearchNeed need = f.OpenNeed(7, 2);
            f.Source.AddPropertyForSale(f.PurchaseOption("opt-1", "farmhouse", 100000));

            var financing = f.NoFinancing(6000);
            financing.HasLenderOffer = true;
            financing.LenderOfferId = "offer-1";
            financing.LenderName = "Frontier Bank";
            financing.LenderOfferAmountCents = 60000;
            financing.LenderOfferRateBps = 800;
            financing.LenderOfferTermDays = 360;
            financing.LenderOfferMonthlyCents = 2500; // bank's figure: 2500c/mo fits the 3000c burden cap

            var composition = new HouseholdCompositionSummary { HouseholdId = 7, AdultCount = 2 };
            NpcPropertyDecision decision = f.Engine.Decide(need, composition, ledger,
                NpcPropertyMotive.Ownership, f.Source, financing, f.Prices, f.Catalog,
                f.Needs, 5, f.Diag);

            Assert.AreEqual(NpcPropertyDecisionKind.BuyExistingProperty, decision.Kind);
            Assert.AreEqual(40000, decision.CashDownCents);
            Assert.AreEqual(60000, decision.FinancedCents);
            Assert.AreEqual(2500, decision.EstimatedMonthlyBurdenCents);
        }
    }
}
