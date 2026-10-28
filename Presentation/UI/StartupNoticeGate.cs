namespace LandLedgers.UI
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-018): the source-side classifier for
    /// General Store startup notices. Audit finding: the October 9
    /// startup messages ("unavailable items, restocking, cashiers,
    /// visibility") trace to four different origins that the old UI
    /// rendered identically, so real failures and noise were
    /// indistinguishable:
    ///
    /// - Legitimate live conditions: the Operations line ("Staff x/y |
    ///   Stock ... | Reorder needed") and "Startup unavailable" blockers
    ///   come from live runtime state. These MUST stay visible — hiding
    ///   them would conceal real economic failure.
    /// - Premature initialization: the many "… unavailable" fallbacks in
    ///   the management panel render when a runtime source is not bound
    ///   yet (new game / restore mid-bind). They are not conditions of
    ///   the economy; they are missing bindings.
    /// - Tutorial remnants: the first-session guidance strip, which the
    ///   ScenarioGuidanceGate now suppresses for non-First-Ledger
    ///   scenarios.
    /// - Ghost ownership: notices about businesses the player does not
    ///   legitimately own (LL-PLAY-001 family). Those must never render
    ///   as live conditions; they are reported, not displayed.
    /// - Debug display: developer-only readouts that must not reach the
    ///   player surface.
    ///
    /// Precedence is fixed: ghost beats debug beats premature-init
    /// beats tutorial-remnant beats show. A suppressed notice is
    /// re-evaluated when bindings complete — suppression is never a
    /// permanent hide for a condition that later becomes real.
    /// </summary>
    public static class StartupNoticeGate
    {
        public enum Verdict
        {
            Show = 0,
            Suppress = 1,
        }

        /// <summary>
        /// Classifies one startup notice. Callers pass what they know;
        /// anything unproven defaults to the cautious branch.
        /// </summary>
        public static Verdict Classify(
            bool runtimesReady,
            bool isTutorialGuidance,
            bool isDebugOnly,
            bool isGhostOwned,
            bool scenarioActive,
            bool firstSessionComplete,
            out string reason)
        {
            if (isGhostOwned)
            {
                reason = "Ghost ownership: report, do not display as a live condition.";
                return Verdict.Suppress;
            }
            if (isDebugOnly)
            {
                reason = "Debug display: not a player-facing notice.";
                return Verdict.Suppress;
            }
            if (!runtimesReady)
            {
                reason = "Premature initialization: re-evaluate once runtimes bind.";
                return Verdict.Suppress;
            }
            if (isTutorialGuidance && (scenarioActive || firstSessionComplete))
            {
                reason = "Tutorial remnant: superseded by scenario objectives or completed first session.";
                return Verdict.Suppress;
            }
            reason = "Legitimate live condition: keep visible.";
            return Verdict.Show;
        }
    }
}
