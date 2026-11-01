using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass XI WS3 (PASS9-003): dispute-claim dedup, persistence, the
    /// distress-sale feed, and diligence redaction. A claim is a record
    /// only: it never creates legal ownership without process.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3PropertyDisputeTests
    {
        [Test]
        public void RecordClaim_SameParcelClaimantBasis_RefusedAsDuplicate()
        {
            var disputes = new PropertyDisputeService();
            var diag = new List<string>();
            DisputeClaim first = disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DisputedRights,
                10, "displaced", diag);
            Assert.IsNotNull(first);
            DisputeClaim second = disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DisputedRights,
                20, "displaced again", diag);
            Assert.AreSame(first, second, "the existing claim stands; no second claim is recorded");
            Assert.AreEqual(1, disputes.Claims.Count);

            // A different basis is a different dispute, not a duplicate.
            DisputeClaim other = disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DefectiveDocumentation,
                20, "paperwork", diag);
            Assert.IsNotNull(other);
            Assert.AreNotSame(first, other);
            Assert.AreEqual(2, disputes.Claims.Count);

            // A different parcel is a different dispute.
            DisputeClaim elsewhere = disputes.RecordClaim("plot-8", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DisputedRights,
                20, "elsewhere", diag);
            Assert.AreEqual(3, disputes.Claims.Count);
            Assert.AreNotSame(first, elsewhere);
        }

        [Test]
        public void RecordClaim_ResolvedClaim_DoesNotBlockFreshClaim()
        {
            var disputes = new PropertyDisputeService();
            var diag = new List<string>();
            DisputeClaim first = disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Open, ClaimBasis.DisputedRights,
                10, "first", diag);
            first.Status = DisputeClaimStatus.Resolved;
            DisputeClaim fresh = disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Open, ClaimBasis.DisputedRights,
                30, "fresh dispute", diag);
            Assert.AreNotSame(first, fresh, "a closed claim does not block a real new dispute");
            Assert.AreEqual(2, disputes.Claims.Count);
        }

        [Test]
        public void Service_SaveLoadRoundTrip_PreservesClaimsAndDedup()
        {
            var disputes = new PropertyDisputeService();
            var diag = new List<string>();
            disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DisputedRights,
                10, "displaced", diag);
            PropertyDisputeService.PropertyDisputeSaveDto dto = disputes.CaptureSaveDto();
            var restored = new PropertyDisputeService();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Claims.Count);
            Assert.AreEqual("plot-7", restored.Claims[0].ParcelId);
            Assert.AreEqual("household:9", restored.Claims[0].ClaimantId);
            // Dedup still holds after restore: the same triple refuses again.
            DisputeClaim again = restored.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DisputedRights,
                20, "again", diag);
            Assert.AreEqual(1, restored.Claims.Count);
            Assert.AreSame(restored.Claims[0], again);
        }

        [Test]
        public void DiligenceClue_ExposesCountsAndCertaintyOnly_NeverClaimantIdentity()
        {
            var disputes = new PropertyDisputeService();
            var diag = new List<string>();
            disputes.RecordClaim("plot-7", "household:9", "household:9",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Contested, ClaimBasis.DisputedRights,
                10, "displaced", diag);
            string clue = QuickDiligenceService.BuildDisputeClue("plot-7", disputes);
            Assert.IsTrue(clue.Length > 0, "a clue exists for the parcel");
            StringAssert.DoesNotContain("household:9", clue, "claimant identity must never leak into diligence");
            StringAssert.DoesNotContain("displaced", clue, "claim notes must never leak into diligence");
            StringAssert.Contains("1 open/contested", clue, "counts are exposed");
        }

        [Test]
        public void DistressSale_WithFedDisputes_RecordsDisplacedTenantClaim_TitleStillTransfersBySale()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var cash = new CreditCashBridge();
            var taxes = new PropertyTaxService();
            var life = new NpcPropertyLifeService();
            var diag = new List<string>();

            var sellerLedger = new HouseholdLedger(7);
            Assert.IsNull(sellerLedger.RecordInflow(0, 20000, HouseholdIncomeSource.OwnerContribution, "seed", "seed", "test"));
            Assert.IsNull(cash.Register("household:7", new HouseholdCashStore(7, sellerLedger)));
            var buyerLedger = new HouseholdLedger(8);
            Assert.IsNull(buyerLedger.RecordInflow(0, 120000, HouseholdIncomeSource.OwnerContribution, "seed", "seed", "test"));
            Assert.IsNull(cash.Register("household:8", new HouseholdCashStore(8, buyerLedger)));

            var titles = new TitleAuthority();
            Assert.IsNotNull(titles.RegisterParcel("parcel-1", "house parcel", 5f, "household:7", TitleBasis.Purchase, 0, diag));
            var housing = new HousingAuthority();
            Building building = housing.RegisterBuilding("bld-parcel-1", "parcel-1", "house", 0, "test house", diag);
            Assert.IsNotNull(building);
            Assert.IsNotNull(housing.DefineSpace(building.BuildingId, 4, diag));
            NpcPropertyHolding holding = life.RegisterHolding("parcel-1", building.BuildingId, 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, diag);
            string spaceId = housing.SpacesForBuilding("bld-parcel-1")[0].SpaceId;
            Assert.IsNotNull(life.EstablishTenancy(housing, holding, spaceId, 9, new List<int> { 201 }, 1500, 30, diag));

            FinancialObligation debt = authority.Create(ids, FinancialObligationKind.Payable,
                "household:7", "creditor", 5000, 10, "terms", "distress debt");
            Assert.IsNotNull(debt);
            authority.MarkDelinquent(debt.ObligationId);

            var disputes = new PropertyDisputeService();
            var context = new NpcDistressSaleContext
            {
                Ids = ids, Authority = authority, Cash = cash, Titles = titles,
                Housing = housing, Workflow = new CreditOfferWorkflow(), Events = new CreditEventLog(),
                TaxService = taxes, BuyerHouseholdId = 8, SalePriceCents = 90000,
            }.FeedDisputes(disputes);

            NpcPropertySaleResult sale = life.TryBrokerSaleFromDistress(holding, context, 50, diag);
            Assert.IsTrue(sale.Sold, sale.FailureReason);
            Assert.AreEqual("household:8", titles.CurrentHolder("parcel-1"),
                "title moves by the sale conveyance, not by any claim");
            Assert.AreEqual(2, disputes.Claims.Count,
                "the displaced tenant's claim plus the chain-break claim (the test registration carries no conveyance instrument)");
            bool tenantClaim = false;
            foreach (DisputeClaim claim in disputes.Claims)
            {
                if (claim.ClaimantId == "household:9" && claim.Basis == ClaimBasis.DisputedRights) tenantClaim = true;
            }
            Assert.IsTrue(tenantClaim, "the displaced tenant's claim is recorded through the fed service");
            Assert.AreNotEqual("household:9", titles.CurrentHolder("parcel-1"),
                "a claim never creates legal ownership without process");
        }

        [Test]
        public void DistressSale_WithoutFedDisputes_StillSells_RecordsNothing()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var cash = new CreditCashBridge();
            var taxes = new PropertyTaxService();
            var life = new NpcPropertyLifeService();
            var diag = new List<string>();
            var sellerLedger = new HouseholdLedger(7);
            Assert.IsNull(sellerLedger.RecordInflow(0, 20000, HouseholdIncomeSource.OwnerContribution, "seed", "seed", "test"));
            Assert.IsNull(cash.Register("household:7", new HouseholdCashStore(7, sellerLedger)));
            var buyerLedger = new HouseholdLedger(8);
            Assert.IsNull(buyerLedger.RecordInflow(0, 120000, HouseholdIncomeSource.OwnerContribution, "seed", "seed", "test"));
            Assert.IsNull(cash.Register("household:8", new HouseholdCashStore(8, buyerLedger)));
            var titles = new TitleAuthority();
            Assert.IsNotNull(titles.RegisterParcel("parcel-1", "house parcel", 5f, "household:7", TitleBasis.Purchase, 0, diag));
            var housing = new HousingAuthority();
            Building building = housing.RegisterBuilding("bld-parcel-1", "parcel-1", "house", 0, "test house", diag);
            Assert.IsNotNull(building);
            NpcPropertyHolding holding = life.RegisterHolding("parcel-1", building.BuildingId, 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, diag);
            FinancialObligation debt = authority.Create(ids, FinancialObligationKind.Payable,
                "household:7", "creditor", 5000, 10, "terms", "distress debt");
            authority.MarkDelinquent(debt.ObligationId);
            var context = new NpcDistressSaleContext
            {
                Ids = ids, Authority = authority, Cash = cash, Titles = titles,
                Housing = housing, Workflow = new CreditOfferWorkflow(), Events = new CreditEventLog(),
                TaxService = taxes, BuyerHouseholdId = 8, SalePriceCents = 90000,
            };
            NpcPropertySaleResult sale = life.TryBrokerSaleFromDistress(holding, context, 50, diag);
            Assert.IsTrue(sale.Sold, sale.FailureReason);
        }
    }
}
