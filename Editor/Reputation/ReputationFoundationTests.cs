using LandLedgers.Reputation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Reputation
{
    public sealed class ReputationFoundationTests
    {
        [Test]
        public void HeadlineReputationUsesWeightedHiddenSubReputations()
        {
            PlayerReputationState state = new PlayerReputationState(
                dealTrust01: 1f,
                lenderTrust01: 0.8f,
                supplierTrust01: 0.4f,
                localSocialTrust01: 0.2f,
                operationalReliability01: 0f);

            float headline = new ReputationEvaluator().EvaluateHeadline01(state);

            Assert.AreEqual(0.6f, headline, 0.001f);
            Assert.AreEqual(60, new ReputationEvaluator().EvaluateHeadline100(state));
        }

        [Test]
        public void ReputationEventsClampSubReputationsAndReturnDeltas()
        {
            PlayerReputationState state = new PlayerReputationState(
                dealTrust01: 0.02f,
                lenderTrust01: 0.5f,
                supplierTrust01: 0.5f,
                localSocialTrust01: 0.02f,
                operationalReliability01: 0.5f);

            ReputationChangeResult result = new ReputationEventApplier().Apply(
                state,
                CreateEvent(ReputationEventType.DealCollapsedByPlayer));

            Assert.AreEqual(0f, state.dealTrust01, 0.001f);
            Assert.AreEqual(0f, state.localSocialTrust01, 0.001f);
            Assert.Greater(result.HeadlineBefore01, result.HeadlineAfter01);
            Assert.AreEqual(2, result.Deltas.Count);
            CollectionAssert.Contains(result.ReasonCodes, ReputationChangeReasonCode.DealReliabilityDamaged);
        }

        [Test]
        public void LoanEventsAffectLenderTrustWithoutTouchingSupplierTrust()
        {
            PlayerReputationState state = new PlayerReputationState(
                dealTrust01: 0.5f,
                lenderTrust01: 0.5f,
                supplierTrust01: 0.5f,
                localSocialTrust01: 0.5f,
                operationalReliability01: 0.5f);

            ReputationChangeResult result = new ReputationEventApplier().Apply(
                state,
                CreateEvent(ReputationEventType.LoanPaymentLate));

            Assert.Less(state.lenderTrust01, 0.5f);
            Assert.AreEqual(0.5f, state.supplierTrust01, 0.001f);
            Assert.AreEqual(1, result.Deltas.Count);
            Assert.AreEqual(ReputationSubcategory.LenderTrust, result.Deltas[0].subcategory);
            CollectionAssert.Contains(result.ReasonCodes, ReputationChangeReasonCode.LoanPaymentConcern);
        }

        [Test]
        public void SupplierEventsAffectSupplierTrustWithoutTouchingLenderTrust()
        {
            PlayerReputationState state = new PlayerReputationState(
                dealTrust01: 0.5f,
                lenderTrust01: 0.5f,
                supplierTrust01: 0.5f,
                localSocialTrust01: 0.5f,
                operationalReliability01: 0.5f);

            ReputationChangeResult result = new ReputationEventApplier().Apply(
                state,
                CreateEvent(ReputationEventType.SupplierInvoiceLate));

            Assert.Less(state.supplierTrust01, 0.5f);
            Assert.AreEqual(0.5f, state.lenderTrust01, 0.001f);
            Assert.AreEqual(1, result.Deltas.Count);
            Assert.AreEqual(ReputationSubcategory.SupplierTrust, result.Deltas[0].subcategory);
            CollectionAssert.Contains(result.ReasonCodes, ReputationChangeReasonCode.SupplierPaymentConcern);
        }

        [Test]
        public void NegotiationExperienceImprovesAptitudeWithoutUsingRawReputation()
        {
            NegotiationAptitudeEvaluator evaluator = new NegotiationAptitudeEvaluator();
            NegotiationAptitudeResult starting = evaluator.Evaluate(new NegotiationAptitudeState());
            NegotiationAptitudeState experienced = new NegotiationAptitudeState();

            for (int i = 0; i < 12; i++)
            {
                experienced.RecordNegotiation(
                    completed: true,
                    difficult: i % 3 == 0,
                    badFaith: false,
                    cleanCounterAccepted: i % 4 == 0,
                    wellStructuredOffer: true);
            }

            NegotiationAptitudeResult result = evaluator.Evaluate(experienced);

            Assert.Greater(result.InformationQuality01, starting.InformationQuality01);
            Assert.Greater(result.OfferCraft01, starting.OfferCraft01);
            Assert.Greater(result.RelationshipHandling01, starting.RelationshipHandling01);
            Assert.Greater(result.DealLeverageModifier, starting.DealLeverageModifier);
        }

        [Test]
        public void BadFaithNegotiationLimitsRelationshipHandling()
        {
            NegotiationAptitudeEvaluator evaluator = new NegotiationAptitudeEvaluator();
            NegotiationAptitudeState clean = new NegotiationAptitudeState();
            NegotiationAptitudeState badFaith = new NegotiationAptitudeState();

            for (int i = 0; i < 10; i++)
            {
                clean.RecordNegotiation(true, true, false, true, true);
                badFaith.RecordNegotiation(true, true, true, false, true);
            }

            Assert.Greater(
                evaluator.Evaluate(clean).RelationshipHandling01,
                evaluator.Evaluate(badFaith).RelationshipHandling01);
        }

        [Test]
        public void InfluenceProfileCombinesReputationAndNegotiationWithoutRuntimeHooks()
        {
            PlayerReputationState reputation = new PlayerReputationState(
                dealTrust01: 0.7f,
                lenderTrust01: 0.6f,
                supplierTrust01: 0.5f,
                localSocialTrust01: 0.4f,
                operationalReliability01: 0.3f);
            NegotiationAptitudeState aptitude = new NegotiationAptitudeState();
            aptitude.RecordNegotiation(true, true, false, true, true);

            ReputationInfluenceProfile profile = new ReputationEvaluator().BuildInfluenceProfile(reputation, aptitude);

            Assert.AreEqual(0.545f, profile.HeadlineReputation01, 0.001f);
            Assert.AreEqual(reputation.dealTrust01, profile.DealTrust01, 0.001f);
            Assert.Greater(profile.NegotiationInformationQuality01, 0.25f);
            Assert.Greater(profile.NegotiationOfferCraft01, 0.25f);
        }

        [Test]
        public void BusinessTroublePropagatesSlowlyToOwnerOperationalAndLocalTrust()
        {
            PlayerReputationState owner = new PlayerReputationState(
                dealTrust01: 0.7f,
                lenderTrust01: 0.7f,
                supplierTrust01: 0.7f,
                localSocialTrust01: 0.7f,
                operationalReliability01: 0.7f);
            BusinessReputationState business = new BusinessReputationState(
                stockReliability01: 0.1f,
                valueFairness01: 0.55f,
                serviceExperience01: 0.2f,
                conditionPresentationTrust01: 0.2f,
                productTradeConfidence01: 0.15f);
            business.RecordStockout("staples", 20, 3, 1f);
            business.RecordStockout("staples", 21, 3, 1f);
            business.RecordStockout("staples", 22, 3, 1f);

            ReputationChangeResult result = new ReputationEvaluator().ApplyBusinessReputationPropagation(
                owner,
                business,
                visibleFailureSeverity01: 1f);

            Assert.Less(owner.operationalReliability01, 0.7f);
            Assert.Less(owner.localSocialTrust01, 0.7f);
            Assert.AreEqual(0.7f, owner.dealTrust01, 0.001f);
            Assert.AreEqual(0.7f, owner.lenderTrust01, 0.001f);
            Assert.Greater(result.HeadlineBefore01, result.HeadlineAfter01);
        }

        [Test]
        public void SingleWeakBusinessDoesNotCollapseOwnerIdentity()
        {
            PlayerReputationState owner = new PlayerReputationState(
                dealTrust01: 0.8f,
                lenderTrust01: 0.8f,
                supplierTrust01: 0.8f,
                localSocialTrust01: 0.8f,
                operationalReliability01: 0.8f);
            BusinessReputationState business = new BusinessReputationState(
                stockReliability01: 0f,
                valueFairness01: 0f,
                serviceExperience01: 0f,
                conditionPresentationTrust01: 0f,
                productTradeConfidence01: 0f);

            ReputationChangeResult result = new ReputationEvaluator().ApplyBusinessReputationPropagation(
                owner,
                business,
                visibleFailureSeverity01: 1f);

            Assert.Greater(owner.localSocialTrust01, 0.76f);
            Assert.Greater(owner.operationalReliability01, 0.76f);
            Assert.Greater(result.HeadlineAfter01, 0.76f);
        }

        private static ReputationEvent CreateEvent(ReputationEventType eventType)
        {
            return new ReputationEvent
            {
                eventType = eventType,
                sourceSystem = "test",
                severity01 = 1f,
                confidence01 = 1f
            };
        }
    }
}
