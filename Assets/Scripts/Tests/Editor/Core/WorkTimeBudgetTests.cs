using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// TTS-1: the minute is the base unit of work; every Person has a daily work-time
    /// budget that tasks book against. Budget exhaustion blocks assignment with a
    /// reason, never silently.
    /// </summary>
    [TestFixture]
    public sealed class WorkTimeBudgetTests
    {
        private static EntityId TestPerson(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        [Test]
        public void MinuteFloor_ZeroOrNegative_ClampsToOne()
        {
            Assert.AreEqual(1, WorkTimeMath.ClampToWholeMinutes(0));
            Assert.AreEqual(1, WorkTimeMath.ClampToWholeMinutes(-40));
            Assert.AreEqual(1, WorkTimeMath.WholeMinutesUp(0f));
            Assert.AreEqual(1, WorkTimeMath.WholeMinutesUp(0.5f));
        }

        [Test]
        public void WholeMinutesUp_PartialMinute_RoundsUp()
        {
            Assert.AreEqual(2, WorkTimeMath.WholeMinutesUp(61f));
            Assert.AreEqual(1, WorkTimeMath.WholeMinutesUp(60f));
        }

        [Test]
        public void TryCommit_ConsumesRemainingBudget()
        {
            var budget = new WorkTimeBudget(TestPerson(1), 600);

            string reason;
            Assert.IsTrue(budget.TryCommit(120, out reason), reason);
            Assert.IsNull(reason);
            Assert.AreEqual(480, budget.MinutesRemaining);
            Assert.AreEqual(120, budget.MinutesCommitted);
        }

        [Test]
        public void TryCommit_BeyondBudget_BlocksWithClearReason()
        {
            var budget = new WorkTimeBudget(TestPerson(2), 60);

            string reason;
            Assert.IsTrue(budget.TryCommit(50, out reason));
            Assert.IsFalse(budget.TryCommit(20, out reason));

            Assert.IsNotNull(reason);
            StringAssert.Contains("10", reason); // remaining minutes named in the reason
            Assert.AreEqual(10, budget.MinutesRemaining);
        }

        [Test]
        public void ReleaseCommitment_ReturnsMinutesToBudget()
        {
            var budget = new WorkTimeBudget(TestPerson(3), 60);

            string reason;
            Assert.IsTrue(budget.TryCommit(50, out reason));
            budget.ReleaseCommitment(50);

            Assert.AreEqual(60, budget.MinutesRemaining);
            Assert.IsTrue(budget.TryCommit(60, out reason), reason);
        }

        [Test]
        public void EnsureDay_Rollover_ResetsBudgets()
        {
            var store = new WorkTimeBudgetStore();
            store.EnsureDay(41);

            string reason;
            Assert.IsTrue(store.TryCommitTask(TestPerson(4), 600, out reason), reason);
            Assert.AreEqual(0, store.GetOrCreate(TestPerson(4)).MinutesRemaining);

            store.EnsureDay(41); // same day: no reset
            Assert.AreEqual(0, store.GetOrCreate(TestPerson(4)).MinutesRemaining);

            store.EnsureDay(42); // new day: fresh budget
            Assert.AreEqual(600, store.GetOrCreate(TestPerson(4)).MinutesRemaining);
        }

        [Test]
        public void RecordWorked_TracksActualMinutes_WithoutDoubleSpendingBudget()
        {
            var budget = new WorkTimeBudget(TestPerson(5), 600);

            string reason;
            Assert.IsTrue(budget.TryCommit(30, out reason));
            budget.RecordWorked(30);

            Assert.AreEqual(30, budget.MinutesWorked);
            Assert.AreEqual(570, budget.MinutesRemaining); // commitment untouched by worked record
        }

        [Test]
        public void NonPersonId_IsRejected()
        {
            var store = new WorkTimeBudgetStore();
            Assert.Throws<System.ArgumentException>(() => store.GetOrCreate(EntityId.For(EntityKind.Business, 1)));
        }
    }
}
