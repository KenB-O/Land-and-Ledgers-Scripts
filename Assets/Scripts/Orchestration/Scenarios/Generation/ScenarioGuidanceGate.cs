namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-004): the source gate that stops
    /// obsolete first-session guidance from competing with real scenario
    /// objectives. Audit finding: FirstSessionGuidanceManager runs its
    /// First Ledger loop (Open Management, Inspect the General Store,
    /// "Hire your first store worker", ...) unconditionally whenever
    /// guidance is enabled — it never consults the active scenario. On
    /// October 9 the owner, playing a scenario other than the First
    /// Ledger, was shown "Hire your first store worker" even though that
    /// step did not fit the selected scenario.
    ///
    /// Rule (pure, testable; the manager consults it in RefreshGuidance):
    /// - Active scenario is the First Ledger ("the-first-ledger") → show.
    ///   Those steps describe that scenario's actual start.
    /// - No scenario id is known (empty: legacy saves and scenes that do
    ///   not bind scenario context yet) → show. Fail-open, so existing
    ///   First Ledger sessions without a bound scenario context do not
    ///   lose guidance silently.
    /// - Any other active scenario id → SUPPRESS. The scenario HUD
    ///   (ScenarioHudReadModel) is authoritative there; first-session
    ///   steps about a General Store the scenario may not grant are
    ///   obsolete guidance, not objectives.
    ///
    /// This gate does not rewrite any guidance text: "Hire your first
    /// store worker" is legitimate inside the First Ledger. Scene-text
    /// remnants (HUD strip authoring, stale prefab text) are Codex work
    /// listed in Docs/Pass12_PlayerExperience_Contracts.md.
    /// </summary>
    public static class ScenarioGuidanceGate
    {
        /// <summary>
        /// True when first-session guidance may be shown for this scenario.
        /// </summary>
        public static bool ShouldShowFirstSessionGuidance(string activeScenarioId)
        {
            if (string.IsNullOrEmpty(activeScenarioId)) return true;
            return string.Equals(activeScenarioId, ScenarioObjectiveService.FirstLedgerScenarioId, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Applies the legacy fail-open rule only when no scenario authority is
        /// available. When a scenario authority exists, its objectives are the
        /// only authoritative player goals, including for First Ledger.
        /// </summary>
        public static bool ShouldShowFirstSessionGuidance(string activeScenarioId, bool scenarioAuthorityAvailable)
        {
            if (scenarioAuthorityAvailable) return false;
            return ShouldShowFirstSessionGuidance(activeScenarioId);
        }

        /// <summary>Human-readable reason for a suppression (empty when shown).</summary>
        public static string SuppressionReason(string activeScenarioId)
        {
            if (ShouldShowFirstSessionGuidance(activeScenarioId)) return string.Empty;
            return "Scenario '" + activeScenarioId + "' has its own objectives; "
                + "first-session guidance is suppressed and the scenario HUD is authoritative.";
        }
    }
}
