namespace LandLedgers.Orchestration.Scenarios.OpenFrontier
{
    /// <summary>
    /// Overnight 2026-11-04: the Open Frontier sandbox scenario identity.
    ///
    /// Open Frontier is the freeform/sandbox choice: it is selectable
    /// without completing, or even starting, the First Ledger. Nothing
    /// in ScenarioService gates one scenario behind another — any
    /// registered, valid ScenarioAsset starts — so no ScenarioDirector
    /// change was needed, and the First Ledger is untouched.
    ///
    /// What this constant is for: the authored asset and any code that
    /// needs to name the scenario share one id instead of a second
    /// invented string (the guidance gate previously drifted exactly
    /// this way with "the-first-ledger").
    ///
    /// What still needs the Editor (Main Scene and scenario assets are
    /// not published in this repo, so nothing here claims it):
    /// - Create the asset: Assets, Create, Land &amp; Ledgers, Scenario
    ///   Asset, with ScenarioId "open-frontier" and DisplayName
    ///   "Open Frontier". No goals or objectives are required; the
    ///   scenario HUD renders an objectives-free asset as
    ///   "SANDBOX — no mandatory objectives", and Validate passes
    ///   with empty goal/objective lists.
    /// - Register it: add the asset to the Main Scene ScenarioDirector's
    ///   scenarioRegistry list, or assign it to a
    ///   ScenarioStartPanelController instance's scenarioAsset field
    ///   for a start-screen choice.
    /// - Dev default: set the Main Scene ScenarioDirector's
    ///   bootScenarioId to "open-frontier" to auto-begin it on Start.
    ///   Leave it empty to choose from the start panel instead.
    ///
    /// Behaviour under Open Frontier, by existing mechanism:
    /// - First-session guidance is suppressed: the guidance gate shows
    ///   First Ledger steps only for "first-ledger" (or no scenario
    ///   context in legacy scenes), and the dev bootstrapper already
    ///   disables the legacy guidance strip outright.
    /// - Campaign dates and state are not rewritten by scenario
    ///   switching; the start panel starts the clock only after a
    ///   scenario is chosen, and saves record the active scenario id
    ///   for restore.
    /// - Caveat: Open Frontier currently reuses the dev opening town
    ///   and world content built by FirstLedgerSliceBootstrapper
    ///   (General Store, starting stake, opening transport). A
    ///   distinct sandbox world seed is future work, not claimed here.
    /// </summary>
    public static class OpenFrontierScenario
    {
        public const string ScenarioId = "open-frontier";
    }
}
