using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Scenarios;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// P4 money journey: the First Ledger scenario promises "a stake of cash"
    /// (FirstLedger.asset tunable 'startingCashCents'). The asset's int tunable
    /// wins when present and positive; otherwise the serialized fallback applies.
    /// </summary>
    [TestFixture]
    public sealed class FirstLedgerStakeTests
    {
        [Test]
        public void AssetTunableWinsOverFallback()
        {
            var tunable = new TunableValue("startingCashCents", 250000);
            Assert.AreEqual(250000, FirstLedgerSliceBootstrapper.ResolveStartingStakeCents(tunable, 999));
        }

        [Test]
        public void FallbackUsedWhenNoTunable()
        {
            Assert.AreEqual(250000, FirstLedgerSliceBootstrapper.ResolveStartingStakeCents(null, 250000));
        }

        [Test]
        public void NonPositiveTunableFallsBack()
        {
            var zero = new TunableValue("startingCashCents", 0);
            var negative = new TunableValue("startingCashCents", -500);
            Assert.AreEqual(250000, FirstLedgerSliceBootstrapper.ResolveStartingStakeCents(zero, 250000));
            Assert.AreEqual(250000, FirstLedgerSliceBootstrapper.ResolveStartingStakeCents(negative, 250000));
        }

        [Test]
        public void WrongKindTunableFallsBack()
        {
            var text = new TunableValue("startingCashCents", "250000");
            Assert.AreEqual(250000, FirstLedgerSliceBootstrapper.ResolveStartingStakeCents(text, 250000));
        }

        [Test]
        public void NegativeFallbackClampsToZero()
        {
            Assert.AreEqual(0, FirstLedgerSliceBootstrapper.ResolveStartingStakeCents(null, -100));
        }
    }
}
