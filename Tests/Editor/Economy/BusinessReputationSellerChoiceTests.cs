using LandLedgers.Reputation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class BusinessReputationSellerChoiceTests
    {
        [Test]
        public void HigherBusinessReputationOutranksOtherwiseIdenticalSeller()
        {
            BusinessReputationSellerChoice choice = new BusinessReputationSellerChoice();
            BusinessReputationSellerChoiceScore strong = choice.Evaluate(CreateInput("strong", CreateReputation(0.82f), 0.8f, 100, 100));
            BusinessReputationSellerChoiceScore weak = choice.Evaluate(CreateInput("weak", CreateReputation(0.32f), 0.8f, 100, 100));

            Assert.Greater(strong.Score01, weak.Score01);
            Assert.Less(BusinessReputationSellerChoice.Compare(strong, weak), 0);
        }

        [Test]
        public void PriceAndStockCanOutweighBusinessReputation()
        {
            BusinessReputationSellerChoice choice = new BusinessReputationSellerChoice();
            BusinessReputationSellerChoiceScore expensiveThinStock = choice.Evaluate(CreateInput(
                "expensive",
                CreateReputation(0.75f),
                categoryStockHealth01: 0.2f,
                unitPriceCents: 220,
                referenceUnitPriceCents: 100));
            BusinessReputationSellerChoiceScore fairStocked = choice.Evaluate(CreateInput(
                "fair",
                CreateReputation(0.55f),
                categoryStockHealth01: 1f,
                unitPriceCents: 100,
                referenceUnitPriceCents: 100));

            Assert.Greater(fairStocked.Score01, expensiveThinStock.Score01);
            Assert.Less(BusinessReputationSellerChoice.Compare(fairStocked, expensiveThinStock), 0);
        }

        [Test]
        public void IneligibleSellerScoresZero()
        {
            BusinessReputationSellerChoice choice = new BusinessReputationSellerChoice();
            BusinessReputationSellerChoiceScore noStock = choice.Evaluate(
                new BusinessReputationSellerChoiceInput(
                    CreateReputation(1f),
                    categoryStockHealth01: 1f,
                    availableUnits: 0,
                    unitPriceCents: 100,
                    referenceUnitPriceCents: 100,
                    runtimeReliability01: 1f,
                    operatingEfficiency01: 1f,
                    displayName: "No Stock",
                    instanceId: "no_stock",
                    candidateIndex: 0));
            BusinessReputationSellerChoiceScore noStaff = choice.Evaluate(
                new BusinessReputationSellerChoiceInput(
                    CreateReputation(1f),
                    categoryStockHealth01: 1f,
                    availableUnits: 10,
                    unitPriceCents: 100,
                    referenceUnitPriceCents: 100,
                    runtimeReliability01: 1f,
                    operatingEfficiency01: 0f,
                    displayName: "No Staff",
                    instanceId: "no_staff",
                    candidateIndex: 1));

            Assert.IsFalse(noStock.Eligible);
            Assert.IsFalse(noStaff.Eligible);
            Assert.AreEqual(0f, noStock.Score01, 0.001f);
            Assert.AreEqual(0f, noStaff.Score01, 0.001f);
        }

        [Test]
        public void RepeatedCategoryStockoutsOverrideBroadBusinessGoodwillForLocalChoice()
        {
            BusinessReputationSellerChoice choice = new BusinessReputationSellerChoice();
            BusinessReputationState troubledHardwareCounter = CreateReputation(0.8f);
            troubledHardwareCounter.RecordStockout("tools", 14, 2, 1f);
            troubledHardwareCounter.RecordStockout("tools", 15, 2, 1f);
            troubledHardwareCounter.RecordStockout("tools", 16, 2, 1f);

            BusinessReputationSellerChoiceScore familiarButUnreliable = choice.Evaluate(
                CreateInput("unreliable", troubledHardwareCounter, 0.9f, 100, 100, "tools"));
            BusinessReputationSellerChoiceScore plainButDependable = choice.Evaluate(
                CreateInput("dependable", CreateReputation(0.6f), 0.9f, 100, 100, "tools"));

            Assert.Greater(plainButDependable.Score01, familiarButUnreliable.Score01);
            Assert.Less(BusinessReputationSellerChoice.Compare(plainButDependable, familiarButUnreliable), 0);
        }

        private static BusinessReputationSellerChoiceInput CreateInput(
            string id,
            BusinessReputationState reputation,
            float categoryStockHealth01,
            int unitPriceCents,
            int referenceUnitPriceCents,
            string categoryId = null)
        {
            return new BusinessReputationSellerChoiceInput(
                reputation,
                categoryStockHealth01,
                12,
                unitPriceCents,
                referenceUnitPriceCents,
                1f,
                1f,
                id,
                id,
                0,
                categoryId);
        }

        private static BusinessReputationState CreateReputation(float value)
        {
            return new BusinessReputationState(
                value,
                value,
                value,
                value,
                value);
        }
    }
}
