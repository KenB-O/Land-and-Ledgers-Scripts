using LandLedgers.Economy.DealTerms;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Valuation;
using LandLedgers.Reputation;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class DealTermFoundationTests
    {
        [Test]
        public void ReputationAdapterMapsSellerFinancingPerformanceWithoutApplyingIt()
        {
            ExpandedDealTerms terms = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    offered = true,
                    principalCents = 240000,
                    downPaymentCents = 60000,
                    annualInterestRateBps = 700,
                    termMonths = 48
                },
                documentationQuality01 = 1f
            };

            ReputationEvent honored = ExpandedDealTermAdapters.BuildSellerFinancingReputationEvent(
                terms,
                300000,
                SellerFinancingPerformance.Honored,
                "seller_001",
                "business_001");
            ReputationEvent missed = ExpandedDealTermAdapters.BuildSellerFinancingReputationEvent(
                terms,
                300000,
                SellerFinancingPerformance.Missed,
                "seller_001",
                "business_001");

            Assert.AreEqual(ReputationEventType.SellerFinancingHonored, honored.eventType);
            Assert.AreEqual(ReputationEventType.SellerFinancingMissed, missed.eventType);
            Assert.AreEqual("expanded_deal_terms", honored.sourceSystem);
            Assert.AreEqual("seller_001", missed.counterpartyId);
            Assert.Greater(missed.severity01, honored.severity01);
        }

        [Test]
        public void RivalProjectionCarriesExpandedTermsThroughOfferTermsOnly()
        {
            AcquisitionOfferTerms baseline = CreateBaselineOffer();
            ExpandedDealTerms expandedTerms = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 180000,
                    annualInterestRateBps = 650,
                    termMonths = 60,
                    sellerRiskTolerance01 = 0.6f
                },
                delayedPossession = new DelayedPossessionTerms
                {
                    delayed = true,
                    minimumDelayDays = 14,
                    maximumDelayDays = 28,
                    source = DelayedPossessionSource.Mutual,
                    sellerContinuityNeed01 = 0.5f
                },
                documentationQuality01 = 0.9f
            };

            AcquisitionOfferTerms projected = ExpandedDealTermAdapters.ProjectRivalOfferTerms(
                baseline,
                expandedTerms,
                rivalCashCertainty01: 0.7f);

            Assert.IsFalse(baseline.expandedDealTerms.HasAnyTerms);
            Assert.IsTrue(projected.expandedDealTerms.HasAnyTerms);
            Assert.IsTrue(projected.sellerFinancingRequested);
            Assert.Greater(projected.contingencyBurden01, baseline.contingencyBurden01);
            Assert.GreaterOrEqual(projected.nonPriceConcessions01, baseline.nonPriceConcessions01);
        }

        [Test]
        public void FinancingAdapterUsesOfferedSellerFinancingOnly()
        {
            ExpandedDealTerms requestedOnly = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    principalCents = 200000
                }
            };
            ExpandedDealTerms offered = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 200000,
                    downPaymentCents = 100000,
                    annualInterestRateBps = 800,
                    termMonths = 60,
                    paymentCadence = SellerFinancingPaymentCadence.Monthly
                }
            };

            bool requestedBuilt = ExpandedDealTermAdapters.TryBuildSellerFinancingLoanTerm(
                requestedOnly,
                300000,
                out _);
            bool offeredBuilt = ExpandedDealTermAdapters.TryBuildSellerFinancingLoanTerm(
                offered,
                300000,
                out LoanTermStructure loanTerm);

            Assert.IsFalse(requestedBuilt);
            Assert.IsTrue(offeredBuilt);
            Assert.AreEqual(200000, loanTerm.principalCents);
            Assert.AreEqual(RepaymentFrequency.Monthly, loanTerm.repaymentFrequency);
        }

        [Test]
        public void ReputationInfluenceCanBuildBuyerAndFinancingProfiles()
        {
            ReputationInfluenceProfile reputation = new ReputationInfluenceProfile(
                headlineReputation01: 0.7f,
                dealTrust01: 0.8f,
                lenderTrust01: 0.6f,
                supplierTrust01: 0.5f,
                localSocialTrust01: 0.65f,
                operationalReliability01: 0.55f,
                negotiationInformationQuality01: 0.45f,
                negotiationOfferCraft01: 0.5f,
                negotiationRelationshipHandling01: 0.75f);

            FinancingApplicantProfile applicant = ExpandedDealTermAdapters.BuildFinancingApplicantProfile(
                "player",
                reputation,
                availableCashCents: 120000,
                weeklyNetCashFlowCents: 9000,
                existingDebtPaymentCents: 1500,
                ownedAssetValueCents: 250000);
            BuyerOfferProfile buyer = ExpandedDealTermAdapters.BuildBuyerProfileFromReputation(
                "player",
                BuyerKind.Player,
                reputation,
                availableCashCents: 120000,
                offerPriceCents: 300000);

            Assert.AreEqual(0.7f, applicant.reputation01, 0.0001f);
            Assert.AreEqual(0.6f, applicant.lenderTrust01, 0.0001f);
            Assert.AreEqual(0.55f, applicant.operationalReliability01, 0.0001f);
            Assert.AreEqual(0.8f, buyer.priorDealReliability01, 0.0001f);
            Assert.Greater(buyer.cashCertainty01, 0.4f);
            Assert.AreEqual(0.75f, buyer.relationshipWithSeller01, 0.0001f);
        }

        [Test]
        public void RivalProjectionDoesNotMutateBaselineOfferTerms()
        {
            AcquisitionOfferTerms baseline = CreateBaselineOffer();
            ExpandedDealTerms expandedTerms = new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true,
                    offered = true,
                    principalCents = 180000,
                    annualInterestRateBps = 650,
                    termMonths = 60,
                    sellerRiskTolerance01 = 0.6f
                },
                documentationQuality01 = 0.9f
            };

            AcquisitionOfferTerms projected = ExpandedDealTermAdapters.ProjectRivalOfferTerms(
                baseline,
                expandedTerms,
                rivalCashCertainty01: 0.7f);

            Assert.AreEqual(300000, baseline.offerPriceCents);
            Assert.AreEqual(30000, baseline.earnestMoneyCents);
            Assert.AreEqual(0.08f, baseline.contingencyBurden01, 0.0001f);
            Assert.AreEqual(0.25f, baseline.nonPriceConcessions01, 0.0001f);
            Assert.IsFalse(baseline.sellerFinancingRequested);
            Assert.IsFalse(baseline.expandedDealTerms.HasAnyTerms);
            Assert.IsTrue(projected.expandedDealTerms.HasAnyTerms);
        }

        private static AcquisitionOfferTerms CreateBaselineOffer()
        {
            return new AcquisitionOfferTerms
            {
                offerPriceCents = 300000,
                earnestMoneyCents = 30000,
                closingSpeed01 = 0.8f,
                contingencyBurden01 = 0.08f,
                inspectionStrictness01 = 0.15f,
                sellerFinancingRequested = false,
                nonPriceConcessions01 = 0.25f
            };
        }
    }
}
