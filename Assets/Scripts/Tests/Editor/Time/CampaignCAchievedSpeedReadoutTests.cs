// Campaign C, package 16 — DEF-061 (no achieved-speed readout),
// Nov 15, 2026.
//
// VERDICT: READOUT SLICE IMPLEMENTED (diagnostic layer only).
// TimeManager computed LastAchievedGameSecondsPerRealSecond every frame
// and pendingGameSeconds accumulated unboundedly, but the achieved value
// had zero production consumers outside TimeManager.cs — the clock could
// fall behind silently and nothing ever said so. This package gives the
// computed value a consumer: AchievedSpeedDiagnostics (plain C#, no Unity
// dependency) formats the readout line and decides the warning, and
// TimeManager surfaces it via DescribeAchievedSpeed() plus a once-per-day
// warning while the pending queue stays past one full game day.
//
// Threshold (from the existing code, not a new policy): the per-frame
// catch-up budget is ShortTickIntervalGameSeconds * maxShortTicksPerFrame
// (900 s * 64 = 57,600 game-seconds at the shipped defaults), which is
// SMALLER than one game day (86,400 game-seconds), so ordinary
// single-frame deferral can never trip a day-scale threshold; it fires
// only when the clock is more than a whole in-game day behind the
// requested schedule. Exactly one day pending is not a warning.
//
// What this package does NOT do: invent a catch-up policy (shed time /
// shed fidelity / fall behind) — that is Check 9 Q6, owner question (t),
// and stays open. No UI is invented; a diagnostic/summary surface only.
// v3.2 note (LL-2026-11-10-003, CONFIRMED TIME DIRECTION): Normal = 24
// real min/day (1440 s), 500x experimental ceiling, 1000x research-only.
// This package invents no speed value.
//
// Harness limits (stated honestly): TimeManager.cs itself is NOT in the
// harness closure, so the thin wiring (DescribeAchievedSpeed,
// TickAchievedSpeedWarning, the Update call) is verified by parse check
// + source review, not by harness execution — the same boundary as C-14
// (controller wiring) and C-15 (Awake line). Everything testable without
// Unity (threshold, day-keyed decision, formatting) is tested here.
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Time
{
    [TestFixture]
    public sealed class CampaignCAchievedSpeedReadoutTests
    {
        private const float GameSecondsPerDay = 86400f;

        [Test]
        public void WarningThreshold_IsOneFullGameDay()
        {
            Assert.AreEqual(86400f, AchievedSpeedDiagnostics.WarningThresholdGameSeconds(86400f), 0.001f,
                "The threshold is the settings' own game day, not a hardcoded constant.");
            Assert.AreEqual(43200f, AchievedSpeedDiagnostics.WarningThresholdGameSeconds(43200f), 0.001f,
                "A shorter configured day moves the threshold with it.");
        }

        [Test]
        public void WarningThreshold_IsAboveOneFrameCatchUpBudget()
        {
            // Shipped defaults: 900 s short tick * 64 ticks/frame = 57,600
            // game-seconds of catch-up per frame. A day-scale threshold
            // (86,400) sits above it, so ordinary single-frame deferral —
            // a hitch that leaves less than one frame's budget pending —
            // can never trip the warning. This pins the design rationale.
            float frameBudget = 900f * 64f;
            Assert.IsTrue(
                AchievedSpeedDiagnostics.WarningThresholdGameSeconds(GameSecondsPerDay) > frameBudget,
                "The threshold must exceed one frame's catch-up budget, or every hitch would warn.");
        }

        [Test]
        public void ExactlyOneDayPending_DoesNotExceedThreshold()
        {
            Assert.IsFalse(
                AchievedSpeedDiagnostics.PendingExceedsWarningThreshold(86400d, GameSecondsPerDay),
                "Exactly one day pending is the boundary, not a warning (strictly greater).");
        }

        [Test]
        public void OneDayPlusOneSecondPending_ExceedsThreshold()
        {
            Assert.IsTrue(
                AchievedSpeedDiagnostics.PendingExceedsWarningThreshold(86401d, GameSecondsPerDay),
                "One second past a full day is a warning.");
        }

        [Test]
        public void ZeroAndNegativePending_DoNotExceedThreshold()
        {
            Assert.IsFalse(AchievedSpeedDiagnostics.PendingExceedsWarningThreshold(0d, GameSecondsPerDay));
            Assert.IsFalse(AchievedSpeedDiagnostics.PendingExceedsWarningThreshold(-100d, GameSecondsPerDay),
                "The helper is total over its inputs even though production clamps pending at zero.");
        }

        [Test]
        public void ShouldWarn_FiresOnNewDayWhenLagging()
        {
            Assert.IsTrue(
                AchievedSpeedDiagnostics.ShouldWarnAchievedSpeedLag(5, -1, 90000d, GameSecondsPerDay),
                "First lagging day warns (no warning recorded yet).");
        }

        [Test]
        public void ShouldWarn_SilentSameDayWhenAlreadyWarned()
        {
            Assert.IsFalse(
                AchievedSpeedDiagnostics.ShouldWarnAchievedSpeedLag(5, 5, 90000d, GameSecondsPerDay),
                "A sustained lag warns once per day, never once per frame.");
        }

        [Test]
        public void ShouldWarn_SilentWhenBelowThreshold()
        {
            Assert.IsFalse(
                AchievedSpeedDiagnostics.ShouldWarnAchievedSpeedLag(6, 5, 100d, GameSecondsPerDay),
                "A drained queue stays silent even on a new day.");
        }

        [Test]
        public void ShouldWarn_FiresAgainNextDayWhileStillLagging()
        {
            Assert.IsTrue(
                AchievedSpeedDiagnostics.ShouldWarnAchievedSpeedLag(6, 5, 90000d, GameSecondsPerDay),
                "A lag that survives overnight warns again the next day.");
        }

        [Test]
        public void Format_IncludesRequestedAchievedPendingDaysAndSpeed()
        {
            string line = AchievedSpeedDiagnostics.FormatAchievedSpeedLine(60f, 45.5f, 90000d, GameSecondsPerDay, "Speed1x");
            Assert.AreEqual(
                "achieved-speed: requested 60.0 game-s/real-s, achieved 45.5 game-s/real-s, pending 90000 game-s (1.04 days), speed Speed1x",
                line);
        }

        [Test]
        public void Format_ZeroPending_ShowsZeroDays()
        {
            string line = AchievedSpeedDiagnostics.FormatAchievedSpeedLine(60f, 60f, 0d, GameSecondsPerDay, "Speed1x");
            Assert.AreEqual(
                "achieved-speed: requested 60.0 game-s/real-s, achieved 60.0 game-s/real-s, pending 0 game-s (0.00 days), speed Speed1x",
                line);
        }

        [Test]
        public void Format_PausedShowsZeroAchieved()
        {
            string line = AchievedSpeedDiagnostics.FormatAchievedSpeedLine(0f, 0f, 0d, GameSecondsPerDay, "Paused");
            Assert.AreEqual(
                "achieved-speed: requested 0.0 game-s/real-s, achieved 0.0 game-s/real-s, pending 0 game-s (0.00 days), speed Paused",
                line);
        }
    }
}
