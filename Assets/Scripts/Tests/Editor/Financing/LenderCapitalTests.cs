using LandLedgers.Economy.Financing;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Financing
{
    /// <summary>
    /// P4 money journey: NX-3B finite lender capital. The frontier bank lends
    /// its OWN capital — loan activations commit against it, exhausted capital
    /// refuses new loans (never conjured), payoff/recovery releases it, and
    /// default leaves it impaired. State survives save/load.
    /// </summary>
    [TestFixture]
    public sealed class LenderCapitalTests
    {
        [Test]
        public void CommitWithinCapitalSucceeds()
        {
            var funds = new PrivateLenderFunds("Test Bank", 2500000);
            string refusal = funds.CommitFunds("c1", "loan-1", 1000000, 0);
            Assert.IsNull(refusal);
            Assert.AreEqual(1500000, funds.AvailableCents());
        }

        [Test]
        public void CommitBeyondCapitalRefuses()
        {
            var funds = new PrivateLenderFunds("Test Bank", 2500000);
            string refusal = funds.CommitFunds("c1", "loan-1", 3000000, 0);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(2500000, funds.AvailableCents());
        }

        [Test]
        public void ReleaseRestoresAvailability()
        {
            var funds = new PrivateLenderFunds("Test Bank", 2500000);
            funds.CommitFunds("c1", "loan-1", 1000000, 0);
            string refusal = funds.ReleaseCommitment("loan-1");
            Assert.IsNull(refusal);
            Assert.AreEqual(2500000, funds.AvailableCents());
        }

        [Test]
        public void SaveDtoRoundTripPreservesCommitments()
        {
            var funds = new PrivateLenderFunds("Test Bank", 2500000);
            funds.CommitFunds("c1", "loan-1", 1000000, 3);
            PrivateLenderFunds.PrivateLenderFundsSaveDto dto = funds.CaptureSaveDto();

            var restored = new PrivateLenderFunds();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual("Test Bank", restored.LenderName);
            Assert.AreEqual(2500000, restored.CapitalCents);
            Assert.AreEqual(1500000, restored.AvailableCents());
        }

        [Test]
        public void DebtManagerCommitWithinCapitalSucceeds()
        {
            GameObject go = new GameObject("debt-test");
            try
            {
                var manager = go.AddComponent<PlayerDebtManager>();
                bool ok = manager.TryCommitLenderCapital("loan-a", 1000000, 0, out string refusal);
                Assert.IsTrue(ok, refusal);
                Assert.IsNull(refusal);
                Assert.AreEqual(1500000, manager.LenderAvailableCapitalCents);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DebtManagerCommitBeyondCapitalRefused()
        {
            GameObject go = new GameObject("debt-test");
            try
            {
                var manager = go.AddComponent<PlayerDebtManager>();
                Assert.IsTrue(manager.TryCommitLenderCapital("loan-a", 2000000, 0, out _));
                bool ok = manager.TryCommitLenderCapital("loan-b", 1000000, 0, out string refusal);
                Assert.IsFalse(ok);
                Assert.IsNotNull(refusal);
                StringAssert.Contains("own capital", refusal);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DebtManagerReleaseFreesCapitalForNextLoan()
        {
            GameObject go = new GameObject("debt-test");
            try
            {
                var manager = go.AddComponent<PlayerDebtManager>();
                Assert.IsTrue(manager.TryCommitLenderCapital("loan-a", 2500000, 0, out _));
                Assert.AreEqual(0, manager.LenderAvailableCapitalCents);
                manager.ReleaseLenderCapital("loan-a");
                Assert.AreEqual(2500000, manager.LenderAvailableCapitalCents);
                Assert.IsTrue(manager.TryCommitLenderCapital("loan-b", 2500000, 1, out _));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DebtManagerLenderFundsSurviveSaveLoad()
        {
            GameObject go = new GameObject("debt-test");
            GameObject go2 = new GameObject("debt-test-2");
            try
            {
                var manager = go.AddComponent<PlayerDebtManager>();
                Assert.IsTrue(manager.TryCommitLenderCapital("loan-a", 1000000, 0, out _));
                LandLedgers.Persistence.PlayerDebtSaveDto dto = manager.CaptureSaveDto();

                var restored = go2.AddComponent<PlayerDebtManager>();
                restored.LoadFromSaveDto(dto);
                Assert.AreEqual(1500000, restored.LenderAvailableCapitalCents);
                Assert.AreEqual(1, restored.LenderFunds.Commitments.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(go2);
            }
        }

        [Test]
        public void DebtManagerLenderStandingTextShowsFreeCapital()
        {
            GameObject go = new GameObject("debt-test");
            try
            {
                var manager = go.AddComponent<PlayerDebtManager>();
                StringAssert.Contains("Lending capital free", manager.BuildLenderStandingText());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
