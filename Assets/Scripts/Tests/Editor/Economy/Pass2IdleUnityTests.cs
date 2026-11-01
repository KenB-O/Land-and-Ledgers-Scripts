using LandLedgers.Population;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass 2 (task system): idle schedule-activity tests.
    /// UNITY-ONLY, AUTHORED, NOT RUN: PersonActivityKind lives in
    /// PersonScheduleTracker.cs, whose full file drags the population
    /// state closure that the small Roslyn harness does not compile.
    /// Run in Unity Test Runner (EditMode) by Codex / Unity AI.
    /// Do NOT quote as Roslyn results.
    /// </summary>
    [TestFixture]
    public sealed class Pass2IdleUnityTests
    {
        [Test]
        public void Idle_IsScheduleActivity_NotBudgetConsuming_InstantlyPreemptible()
        {
            Assert.AreEqual(14, (int)PersonActivityKind.Idle);
            Assert.IsTrue(PersonActivityPolicy.IsInterruptible(PersonActivityKind.Idle));
            Assert.IsFalse(PersonActivityPolicy.ConsumesWorkBudget(PersonActivityKind.Idle));
            Assert.AreEqual(0, Pass2IdleActivity.HiringPressureMinutesExcludingIdle(120, 120));
            Assert.AreEqual(30, Pass2IdleActivity.HiringPressureMinutesExcludingIdle(120, 90));
        }
    }
}
