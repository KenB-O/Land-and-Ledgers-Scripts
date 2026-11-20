using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class GeneralStoreServiceClerkPolicyTests
    {
        [Test]
        public void OwnerOperatorCanServeWithoutClerkSlot()
        {
            CollectionAssert.AreEqual(
                new[] { 7 },
                GeneralStoreServiceClerkPolicy.CandidatePersonIds(-1, 7));
            Assert.IsTrue(GeneralStoreServiceClerkPolicy.IsCandidate(7, -1, 7));
        }

        [Test]
        public void AssignedClerkIsPreferredAndOwnerRemainsFallback()
        {
            CollectionAssert.AreEqual(
                new[] { 3, 7 },
                GeneralStoreServiceClerkPolicy.CandidatePersonIds(3, 7));
        }

        [Test]
        public void InvalidAndDuplicateCandidatesAreOmitted()
        {
            List<int> candidates = GeneralStoreServiceClerkPolicy.CandidatePersonIds(7, 7);
            CollectionAssert.AreEqual(new[] { 7 }, candidates);
            Assert.IsFalse(GeneralStoreServiceClerkPolicy.IsCandidate(-1, 7, 7));
        }
    }
}
