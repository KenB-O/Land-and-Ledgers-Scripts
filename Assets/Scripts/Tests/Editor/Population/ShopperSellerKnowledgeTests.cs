// Wiring pass (N-2, Nov 20, 2026): tests for ShopperSellerKnowledge.KnowsSupplier,
// the any-resident query behind the Business Operations Monitor's live D-18 value.
// Lane 9e seeds dealer knowledge; the monitor reads it through
// SimulationDrivers.AnyHouseholdKnowsSeller instead of a hardcoded false.
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class ShopperSellerKnowledgeTests
    {
        [Test]
        public void KnowsSupplier_TrueWhenAnyResidentKnowsTheSupplier()
        {
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(1, "fuel-dealer-1", "local fuel dealer", 5);

            Assert.IsTrue(knowledge.KnowsSupplier("fuel-dealer-1"));
        }

        [Test]
        public void KnowsSupplier_FalseWhenNobodyKnowsTheSupplier()
        {
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(1, "bakery-1", "local bakery", 5);

            Assert.IsFalse(knowledge.KnowsSupplier("fuel-dealer-1"));
        }

        [Test]
        public void KnowsSupplier_FalseOnEmptyKnowledge_NullOrBlankId()
        {
            var knowledge = new ShopperSellerKnowledge();

            Assert.IsFalse(knowledge.KnowsSupplier("fuel-dealer-1"));
            Assert.IsFalse(knowledge.KnowsSupplier(null));
            Assert.IsFalse(knowledge.KnowsSupplier("   "));
        }

        [Test]
        public void KnowsSupplier_CaseInsensitive_MatchingKnows()
        {
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(2, "Fuel-Dealer-1", "local fuel dealer", 5);

            Assert.IsTrue(knowledge.KnowsSupplier("fuel-dealer-1"));
            Assert.IsTrue(knowledge.KnowsSupplier("FUEL-DEALER-1"));
        }

        [Test]
        public void KnowsSupplier_TrueRegardlessOfWhichResidentKnows()
        {
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(7, "fuel-dealer-1", "local fuel dealer", 5);

            // Knows() is per-person; KnowsSupplier() is any-resident — the
            // monitor asks whether the dealer is known at all, not by whom.
            Assert.IsFalse(knowledge.Knows(1, "fuel-dealer-1"));
            Assert.IsTrue(knowledge.KnowsSupplier("fuel-dealer-1"));
        }
    }
}
