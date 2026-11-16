// Campaign C (DEF-023) — a skipped shopping-trip rehydration must fail loudly
// instead of silently dropping in-flight trips. Tests ShoppingRehydrateGuard
// directly: it is pure state with no Unity dependency, so the Roslyn harness
// can exercise it without compiling SimulationDrivers. The production wiring
// (ApplyRestoredShoppingTrips records the skip, OnShortTick retries once and
// shields the first tick's backstop) is verified by Roslyn syntax-parse plus
// inspection — SimulationDrivers is too large a closure for the focused
// harness. No TaskAuthority fixture is used here, so the package-1
// two-authorities test artifact cannot recur.
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCRehydrateTimingTests
    {
        [Test]
        public void FreshGuard_IsNotSkippedAndGuardsNothing()
        {
            var guard = new ShoppingRehydrateGuard();
            Assert.IsFalse(guard.SkippedAfterLoad);
            Assert.AreEqual(0, guard.SkippedTripCount);
            Assert.IsFalse(guard.GuardedFirstTick);
            Assert.IsFalse(guard.TakeFirstTickGuard());
        }

        [Test]
        public void RecordSkipped_FirstCall_ArmsSkipAndReturnsTrue()
        {
            var guard = new ShoppingRehydrateGuard();
            Assert.IsTrue(guard.RecordSkipped(2));
            Assert.IsTrue(guard.SkippedAfterLoad);
            Assert.AreEqual(2, guard.SkippedTripCount);
            Assert.IsFalse(guard.GuardedFirstTick);
        }

        [Test]
        public void RecordSkipped_SecondCallWhileSkipped_ReturnsFalseAndKeepsGuardUnconsumed()
        {
            var guard = new ShoppingRehydrateGuard();
            Assert.IsTrue(guard.RecordSkipped(2));
            // A late retry on the next tick re-records the same skip: no new
            // loud error, and the one-time first-tick shield must NOT re-arm
            // (the backstop may be skipped once, never once per tick).
            Assert.IsFalse(guard.RecordSkipped(2));
            Assert.IsTrue(guard.SkippedAfterLoad);
            Assert.IsFalse(guard.GuardedFirstTick);
            Assert.IsTrue(guard.TakeFirstTickGuard());
        }

        [Test]
        public void RecordSkipped_WhileSkipped_RefreshesCount()
        {
            var guard = new ShoppingRehydrateGuard();
            Assert.IsTrue(guard.RecordSkipped(1));
            Assert.IsFalse(guard.RecordSkipped(3));
            Assert.AreEqual(3, guard.SkippedTripCount);
        }

        [Test]
        public void TakeFirstTickGuard_TrueExactlyOncePerSkip()
        {
            var guard = new ShoppingRehydrateGuard();
            guard.RecordSkipped(1);
            Assert.IsTrue(guard.TakeFirstTickGuard());
            Assert.IsTrue(guard.GuardedFirstTick);
            Assert.IsFalse(guard.TakeFirstTickGuard());
            Assert.IsFalse(guard.TakeFirstTickGuard());
        }

        [Test]
        public void TakeFirstTickGuard_RearmsAfterClearAndNewSkip()
        {
            var guard = new ShoppingRehydrateGuard();
            guard.RecordSkipped(1);
            Assert.IsTrue(guard.TakeFirstTickGuard());
            guard.RecordCleared();
            Assert.IsFalse(guard.TakeFirstTickGuard());
            Assert.IsTrue(guard.RecordSkipped(2));
            Assert.IsTrue(guard.TakeFirstTickGuard());
            Assert.IsFalse(guard.TakeFirstTickGuard());
        }

        [Test]
        public void RecordCleared_ResetsAllState()
        {
            var guard = new ShoppingRehydrateGuard();
            guard.RecordSkipped(2);
            Assert.IsTrue(guard.TakeFirstTickGuard());
            guard.RecordCleared();
            Assert.IsFalse(guard.SkippedAfterLoad);
            Assert.AreEqual(0, guard.SkippedTripCount);
            Assert.IsFalse(guard.GuardedFirstTick);
            Assert.IsFalse(guard.TakeFirstTickGuard());
        }

        [Test]
        public void RecordCleared_OnFreshGuard_IsNoop()
        {
            var guard = new ShoppingRehydrateGuard();
            guard.RecordCleared();
            Assert.IsFalse(guard.SkippedAfterLoad);
            Assert.AreEqual(0, guard.SkippedTripCount);
            Assert.IsFalse(guard.TakeFirstTickGuard());
        }

        [Test]
        public void RecordSkipped_NegativeCount_ClampsToZero()
        {
            var guard = new ShoppingRehydrateGuard();
            Assert.IsTrue(guard.RecordSkipped(-5));
            Assert.AreEqual(0, guard.SkippedTripCount);
            Assert.IsTrue(guard.SkippedAfterLoad);
        }
    }
}
