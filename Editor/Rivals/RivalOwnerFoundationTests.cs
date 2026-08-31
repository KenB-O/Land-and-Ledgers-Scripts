using System.Collections.Generic;
using LandLedgers.Economy.Rivals;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Rivals
{
    public sealed class RivalOwnerFoundationTests
    {
        [Test]
        public void RivalScaleIncreasesWithCapitalHoldingsAndBusinesses()
        {
            RivalOwnerScaleEvaluator evaluator = new RivalOwnerScaleEvaluator();
            RivalOwnerScaleSnapshot small = evaluator.Evaluate("small", CreateScaleInputs(12000, 9000, 1, 1, 0.2f));
            RivalOwnerScaleSnapshot large = evaluator.Evaluate("large", CreateScaleInputs(45000, 52000, 3, 4, 0.2f));

            Assert.Greater(large.TotalScaleScore01, small.TotalScaleScore01);
            Assert.Greater(large.ThreatToPlayer01, small.ThreatToPlayer01);
            Assert.Greater(large.BusinessCount, small.BusinessCount);
        }

        [Test]
        public void ThreatFallsWhenPlayerOutscalesSameRival()
        {
            RivalOwnerScaleEvaluator evaluator = new RivalOwnerScaleEvaluator();
            RivalScaleInputs nearPlayer = CreateScaleInputs(30000, 36000, 2, 2, 0.5f);
            RivalScaleInputs outscaledByPlayer = nearPlayer;
            outscaledByPlayer.playerCapitalCents = 120000;
            outscaledByPlayer.playerHoldingValueCents = 160000;
            outscaledByPlayer.playerBusinessCount = 8;
            outscaledByPlayer.playerLandCount = 10;

            RivalOwnerScaleSnapshot strongerThreat = evaluator.Evaluate("rival", nearPlayer);
            RivalOwnerScaleSnapshot weakerThreat = evaluator.Evaluate("rival", outscaledByPlayer);

            Assert.Greater(strongerThreat.ThreatToPlayer01, weakerThreat.ThreatToPlayer01);
            Assert.AreEqual(RivalImportanceTier.ActiveRival, RivalOwnerScaleEvaluator.GetTier(0.5f));
            Assert.AreEqual(RivalImportanceTier.MajorRival, RivalOwnerScaleEvaluator.GetTier(0.75f));
        }

        [Test]
        public void HigherAggressionProducesMoreActiveProposal()
        {
            RivalOwnerRuntimeState owner = CreateOwner(60000);
            RivalOpportunitySignal opportunity = CreateOpportunity("plot_001", RivalOpportunityKind.Land, 28000, 26000, 0.48f);
            RivalOwnerScaleSnapshot scale = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(60000, 50000, 2, 2, 0.55f));
            RivalOwnerDecisionEvaluator evaluator = new RivalOwnerDecisionEvaluator();

            RivalDecisionProposal patient = evaluator.Evaluate(owner, CreateTraits(0.05f, 0.65f), scale, new[] { opportunity })[0];
            RivalDecisionProposal aggressive = evaluator.Evaluate(owner, CreateTraits(0.95f, 0.65f), scale, new[] { opportunity })[0];

            Assert.GreaterOrEqual((int)aggressive.Action, (int)patient.Action);
            Assert.Greater(aggressive.MaxBidCents, patient.MaxBidCents);
        }

        [Test]
        public void BetterDecisionQualityRanksStrongOpportunityAboveWeakOpportunity()
        {
            RivalOwnerRuntimeState owner = CreateOwner(100000);
            RivalOpportunitySignal strong = CreateOpportunity("strong", RivalOpportunityKind.BusinessAcquisition, 70000, 48000, 0.85f);
            RivalOpportunitySignal weak = CreateOpportunity("weak", RivalOpportunityKind.BusinessAcquisition, 42000, 46000, 0.2f);
            RivalOwnerScaleSnapshot scale = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(70000, 70000, 3, 2, 0.65f));
            RivalOwnerDecisionEvaluator evaluator = new RivalOwnerDecisionEvaluator();

            IReadOnlyList<RivalDecisionProposal> proposals = evaluator.Evaluate(owner, CreateTraits(0.5f, 1f), scale, new[] { weak, strong });

            Assert.AreEqual("strong", proposals[0].Opportunity.opportunityId);
            Assert.Greater(proposals[0].Score01, proposals[1].Score01);
        }

        [Test]
        public void RivalCannotBidBeyondCapitalAfterReserveRules()
        {
            RivalOwnerRuntimeState owner = CreateOwner(18000);
            RivalOpportunitySignal opportunity = CreateOpportunity("expensive", RivalOpportunityKind.Land, 50000, 42000, 0.9f);
            RivalOwnerScaleSnapshot scale = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(18000, 12000, 1, 1, 0.2f));
            RivalOwnerTraits traits = CreateTraits(1f, 0.8f);
            traits.liquidityDiscipline01 = 1f;

            RivalDecisionProposal proposal = new RivalOwnerDecisionEvaluator().Evaluate(owner, traits, scale, new[] { opportunity })[0];

            Assert.LessOrEqual(proposal.MaxBidCents, owner.AvailableCapitalCents);
            Assert.Less(proposal.CashCoverage01, 1f);
            Assert.AreNotEqual(RivalDecisionAction.Bid, proposal.Action);
        }

        [Test]
        public void BidPressureIncreasesWithAggressionUrgencyFitAndThreat()
        {
            RivalOwnerRuntimeState owner = CreateOwner(90000);
            RivalOpportunitySignal opportunity = CreateOpportunity("store", RivalOpportunityKind.BusinessAcquisition, 52000, 50000, 0.82f);
            RivalOwnerScaleSnapshot lowerThreat = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(20000, 20000, 1, 1, 0.1f));
            RivalOwnerScaleSnapshot higherThreat = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(90000, 80000, 4, 4, 0.9f));
            RivalOwnerTraits lowTraits = CreateTraits(0.1f, 0.8f);
            RivalOwnerTraits highTraits = CreateTraits(1f, 0.8f);
            RivalOwnerDecisionEvaluator decisionEvaluator = new RivalOwnerDecisionEvaluator();
            RivalBidPressureEvaluator bidEvaluator = new RivalBidPressureEvaluator();

            RivalDecisionProposal lowProposal = decisionEvaluator.Evaluate(owner, lowTraits, lowerThreat, new[] { opportunity })[0];
            RivalDecisionProposal highProposal = decisionEvaluator.Evaluate(owner, highTraits, higherThreat, new[] { opportunity })[0];
            RivalBidPressureResult low = bidEvaluator.Evaluate(owner, lowTraits, lowerThreat, lowProposal);
            RivalBidPressureResult high = bidEvaluator.Evaluate(owner, highTraits, higherThreat, highProposal);

            Assert.Greater(high.BidPressure01, low.BidPressure01);
            Assert.Greater(high.OfferTerms.closingSpeed01, low.OfferTerms.closingSpeed01);
            Assert.AreEqual(LandLedgers.Economy.Valuation.BuyerKind.Rival, high.BuyerProfile.buyerKind);
        }

        [Test]
        public void MarketPressureAggregatesReadOnlyProposalOutputs()
        {
            RivalOwnerRuntimeState owner = CreateOwner(80000);
            RivalOwnerTraits traits = CreateTraits(1f, 0.8f);
            RivalOwnerScaleSnapshot scale = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(80000, 75000, 3, 3, 0.8f));
            RivalOpportunitySignal land = CreateOpportunity("land", RivalOpportunityKind.Land, 34000, 30000, 0.8f);
            RivalOpportunitySignal business = CreateOpportunity("business", RivalOpportunityKind.BusinessAcquisition, 60000, 52000, 0.82f);
            RivalOwnerDecisionEvaluator decisionEvaluator = new RivalOwnerDecisionEvaluator();
            List<RivalDecisionProposal> proposals = new List<RivalDecisionProposal>();
            proposals.AddRange(decisionEvaluator.Evaluate(owner, traits, scale, new[] { land, business }));

            MarketPressureSnapshot landPressure = new MarketPressureEvaluator().Evaluate(
                "town_market",
                RivalOpportunityKind.Land,
                string.Empty,
                proposals,
                new[] { scale });

            Assert.Greater(landPressure.LandDemandPressure01, 0f);
            Assert.Greater(landPressure.LocalCompetitionPressure01, 0f);
            CollectionAssert.Contains((System.Collections.ICollection)landPressure.ReasonCodes, MarketPressureReasonCode.RivalLandDemand);
        }

        [Test]
        public void ExpiredPlayerWindowPressureProducesRivalBidProposal()
        {
            RivalOwnerRuntimeState owner = CreateOwner(90000);
            RivalOwnerTraits traits = CreateTraits(0.9f, 0.85f);
            RivalOwnerScaleSnapshot scale = new RivalOwnerScaleEvaluator().Evaluate("rival", CreateScaleInputs(90000, 75000, 3, 3, 0.8f));
            RivalOpportunitySignal expiredLand = new RivalOpportunitySignal
            {
                opportunityId = "land_rough_lodging_pressure",
                kind = RivalOpportunityKind.Land,
                categoryId = "Residential",
                estimatedValueCents = 36000,
                askingPriceCents = 30000,
                marketPressure01 = 0.86f,
                strategicFit01 = 0.82f,
                urgency01 = 0.9f,
                score01 = 0.88f
            };

            RivalDecisionProposal proposal = new RivalOwnerDecisionEvaluator().Evaluate(owner, traits, scale, new[] { expiredLand })[0];

            Assert.AreEqual(RivalDecisionAction.Bid, proposal.Action);
            Assert.Greater(proposal.MaxBidCents, expiredLand.askingPriceCents);
            CollectionAssert.Contains((System.Collections.ICollection)proposal.ReasonCodes, "strategic_fit");
        }

        private static RivalScaleInputs CreateScaleInputs(int rivalCapital, int rivalHoldings, int rivalBusinesses, int rivalLand, float overlap)
        {
            return new RivalScaleInputs
            {
                rivalCapitalCents = rivalCapital,
                rivalHoldingValueCents = rivalHoldings,
                rivalBusinessCount = rivalBusinesses,
                rivalLandCount = rivalLand,
                playerCapitalCents = 50000,
                playerHoldingValueCents = 60000,
                playerBusinessCount = 3,
                playerLandCount = 4,
                baselineTownCapitalCents = 25000,
                baselineHoldingValueCents = 25000,
                opportunityOverlap01 = overlap
            };
        }

        private static RivalOwnerRuntimeState CreateOwner(int capitalCents)
        {
            return new RivalOwnerRuntimeState
            {
                ownerId = "rival",
                capitalCents = capitalCents,
                reservedCapitalCents = 0,
                reputation01 = 0.55f,
                localTrust01 = 0.45f,
                priorDealReliability01 = 0.65f,
                communityFit01 = 0.5f
            };
        }

        private static RivalOwnerTraits CreateTraits(float aggression, float decisionQuality)
        {
            RivalOwnerTraits traits = RivalOwnerTraits.Balanced;
            traits.aggression01 = aggression;
            traits.decisionQuality01 = decisionQuality;
            traits.riskTolerance01 = 0.5f;
            traits.liquidityDiscipline01 = 0.5f;
            traits.patience01 = 0.5f;
            traits.expansionBias01 = 0.5f;
            return traits;
        }

        private static RivalOpportunitySignal CreateOpportunity(string id, RivalOpportunityKind kind, int estimatedValue, int askingPrice, float fit)
        {
            return new RivalOpportunitySignal
            {
                opportunityId = id,
                kind = kind,
                categoryId = kind.ToString(),
                estimatedValueCents = estimatedValue,
                askingPriceCents = askingPrice,
                marketPressure01 = 0.55f,
                strategicFit01 = fit,
                urgency01 = fit,
                score01 = 0f
            };
        }
    }
}
