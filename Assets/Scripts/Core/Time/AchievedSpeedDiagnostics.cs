// Campaign C (DEF-061): no achieved-speed readout — readout slice only,
// Nov 15, 2026.
//
// DEF-061 as audited (C package 13): TimeManager computes
// LastAchievedGameSecondsPerRealSecond every frame and pendingGameSeconds
// accumulates unboundedly (TimeManager.cs:540, drained at most
// min(pending, budget) per frame at :546), but the achieved value had zero
// production consumers outside TimeManager.cs — the clock could fall behind
// silently and nothing ever said so. This file is the readout slice: a
// pure diagnostic surface for the computed achieved speed, plus the
// threshold and day-keyed decision for the warning. It invents no catch-up
// policy (shed time / shed fidelity / fall behind) — that is Check 9 Q6,
// owner question (t), and stays open. No UI is invented; a
// diagnostic/summary surface only.
//
// Threshold justification (from the existing code, not a new policy):
// the per-frame catch-up budget is ShortTickIntervalGameSeconds *
// maxShortTicksPerFrame — 900 s * 64 = 57,600 game-seconds at the shipped
// defaults — which is SMALLER than one game day (GameSecondsPerDay =
// 86,400 game-seconds). A threshold of one full game day therefore cannot
// trip on ordinary single-frame deferral (a hitch that leaves less than a
// frame's budget pending drains next frame); it fires only when the clock
// is more than a whole in-game day behind the requested schedule. The day
// is the simulation's own unit, and pendingGameSeconds is persisted across
// save/load (TimeSaveDto.pendingGameSeconds), so the threshold is
// meaningful across a Continue. Exactly one day pending is not a warning
// (strictly greater); the warning is day-keyed (once per absolute day,
// LogRefusalDayKeyed precedent) so a sustained lag warns once a day
// instead of once a frame.
//
// v3.2 note (LL-2026-11-10-003, CONFIRMED TIME DIRECTION): Normal = 24
// real min/day (1440 s), Real Time 1x and Observe 10x are targets, 500x
// Normal is an experimental ceiling candidate, 1000x research-only, and
// the strategic fast-forward split is a design direction (not
// implemented). This file invents no speed value: the threshold is the
// settings' own GameSecondsPerDay, and the readout reports whatever
// requested/achieved rates the live settings produce.
//
// Plain C#, no Unity dependency, so the Roslyn harness can execute it
// directly. The TimeManager wiring (DescribeAchievedSpeed + the
// once-per-day Debug.LogWarning call in Update) is thin and verified by
// parse check + source review; TimeManager itself is not in the harness
// closure.
using System.Globalization;

namespace LandLedgers.Time
{
    /// <summary>
    /// Diagnostic-only readout for the achieved simulation speed (DEF-061).
    /// Pure functions over values the clock already computes; no state, no
    /// catch-up policy, no UI.
    /// </summary>
    public static class AchievedSpeedDiagnostics
    {
        /// <summary>
        /// The pending-queue warning threshold, in game-seconds: one full
        /// game day. See the file header for why a day, and why it cannot
        /// trip on ordinary single-frame deferral.
        /// </summary>
        public static float WarningThresholdGameSeconds(float gameSecondsPerDay)
        {
            return gameSecondsPerDay;
        }

        /// <summary>
        /// True when the pending queue holds more than one full game day of
        /// unprocessed time. Exactly one day pending is not a warning.
        /// </summary>
        public static bool PendingExceedsWarningThreshold(double pendingGameSeconds, float gameSecondsPerDay)
        {
            return pendingGameSeconds > gameSecondsPerDay;
        }

        /// <summary>
        /// Day-keyed warning decision: warn once per absolute day while the
        /// pending queue stays past the threshold. A sustained lag warns
        /// once a day, never once a frame.
        /// </summary>
        public static bool ShouldWarnAchievedSpeedLag(
            int absoluteDayIndex,
            int lastWarnedDayIndex,
            double pendingGameSeconds,
            float gameSecondsPerDay)
        {
            return absoluteDayIndex != lastWarnedDayIndex
                && PendingExceedsWarningThreshold(pendingGameSeconds, gameSecondsPerDay);
        }

        /// <summary>
        /// The diagnostic line: requested vs achieved game-seconds per real
        /// second, pending game-seconds (and days), and the selected speed.
        /// Invariant culture so the line is byte-stable in logs.
        /// </summary>
        public static string FormatAchievedSpeedLine(
            float requestedGameSecondsPerRealSecond,
            float achievedGameSecondsPerRealSecond,
            double pendingGameSeconds,
            float gameSecondsPerDay,
            string speedName)
        {
            double pendingDays = gameSecondsPerDay > 0f
                ? pendingGameSeconds / gameSecondsPerDay
                : 0d;
            return string.Format(
                CultureInfo.InvariantCulture,
                "achieved-speed: requested {0:F1} game-s/real-s, achieved {1:F1} game-s/real-s, pending {2:F0} game-s ({3:F2} days), speed {4}",
                requestedGameSecondsPerRealSecond,
                achievedGameSecondsPerRealSecond,
                pendingGameSeconds,
                pendingDays,
                speedName);
        }
    }
}
