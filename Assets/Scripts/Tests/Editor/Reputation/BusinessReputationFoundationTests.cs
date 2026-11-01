using LandLedgers.Reputation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Reputation
{
    public sealed class BusinessReputationFoundationTests
    {
        [Test]
        public void HeadlineBusinessReputationUsesWeightedBusinessSignalsAndClamps()
        {
            BusinessReputationState state = new BusinessReputationState(
                stockReliability01: 1.2f,
                valueFairness01: 0.8f,
                serviceExperience01: 0.4f,
                conditionPresentationTrust01: 0.2f,
                productTradeConfidence01: -0.4f);

            BusinessReputationEvaluator evaluator = new BusinessReputationEvaluator();

            Assert.AreEqual(0.52f, evaluator.EvaluateHeadline01(state), 0.001f);
            Assert.AreEqual(52, evaluator.EvaluateHeadline100(state));
            Assert.AreEqual(1f, state.stockReliability01, 0.001f);
            Assert.AreEqual(0f, state.productTradeConfidence01, 0.001f);
        }

        [Test]
        public void RuntimeObservationImprovesFairCompleteReliableSale()
        {
            BusinessReputationState state = new BusinessReputationState(
                stockReliability01: 0.5f,
                valueFairness01: 0.5f,
                serviceExperience01: 0.5f,
                conditionPresentationTrust01: 0.5f,
                productTradeConfidence01: 0.5f);
            BusinessReputationEvaluator evaluator = new BusinessReputationEvaluator();
            float before = evaluator.EvaluateHeadline01(state);

            evaluator.ApplyObservation(
                state,
                new BusinessReputationObservation(
                    requestedUnits: 4,
                    soldUnits: 4,
                    unitPriceCents: 100,
                    referenceUnitPriceCents: 100,
                    stockHealthAfterSale01: 0.82f,
                    runtimeReliability01: 1f,
                    operatingEfficiency01: 1f));

            Assert.Greater(evaluator.EvaluateHeadline01(state), before);
            Assert.Greater(state.stockReliability01, 0.5f);
            Assert.Greater(state.serviceExperience01, 0.5f);
            Assert.Greater(state.conditionPresentationTrust01, 0.5f);
        }

        [Test]
        public void RuntimeObservationDegradesHighPricePartialUnreliableSale()
        {
            BusinessReputationState state = new BusinessReputationState(
                stockReliability01: 0.75f,
                valueFairness01: 0.75f,
                serviceExperience01: 0.75f,
                conditionPresentationTrust01: 0.75f,
                productTradeConfidence01: 0.75f);
            BusinessReputationEvaluator evaluator = new BusinessReputationEvaluator();
            float before = evaluator.EvaluateHeadline01(state);

            evaluator.ApplyObservation(
                state,
                new BusinessReputationObservation(
                    requestedUnits: 10,
                    soldUnits: 2,
                    unitPriceCents: 250,
                    referenceUnitPriceCents: 100,
                    stockHealthAfterSale01: 0.1f,
                    runtimeReliability01: 0.2f,
                    operatingEfficiency01: 0.25f));

            Assert.Less(evaluator.EvaluateHeadline01(state), before);
            Assert.Less(state.stockReliability01, 0.75f);
            Assert.Less(state.valueFairness01, 0.75f);
            Assert.Less(state.serviceExperience01, 0.75f);
            Assert.Less(state.productTradeConfidence01, 0.75f);
        }
    }
}
