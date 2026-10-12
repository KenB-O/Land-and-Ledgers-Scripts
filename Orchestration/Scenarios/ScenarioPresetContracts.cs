using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// Pass V P1-1 contract: distinct scenario lifecycle operations. Each has
    /// documented generation/restore semantics; see ScenarioLifecycleSemantics.
    /// APPEND-ONLY.
    /// </summary>
    public enum ScenarioLifecycleAction
    {
        NewGame = 0,        // Generate one realized world from definition + seed.
        LoadGame = 1,       // Restore the saved realized world; never regenerate.
        RestartSameSeed = 2,// Regenerate deterministically from the same seed.
        RestartNewSeed = 3, // Regenerate from a new seed.
        ReplayFromPreset = 4,// Fresh campaign from the selected preset.
        ContinueFailed = 5, // Preserve the current world; never secretly regenerate.
    }

    /// <summary>
    /// Pass V P1-1 contract: authored starting-world configuration for a scenario.
    /// This is what the designer wants the starting world to satisfy. It is NOT the
    /// live world and must never become a second economic ledger.
    /// Population ranges must honor the Canon calibration (55-95 residents); authored
    /// scenarios may define their own explicit ranges where consistent with scenario
    /// rules.
    /// </summary>
    [Serializable]
    public sealed class StartingWorldConfiguration
    {
        [SerializeField] private string scenarioId = string.Empty;
        [SerializeField] private string definitionVersion = "1.0.0";
        [SerializeField] private int startYear = 1880;
        [SerializeField] private int startMonth = 3;
        [SerializeField] private int startDay = 1;

        [SerializeField] private int minResidents = 55;
        [SerializeField] private int maxResidents = 95;
        [SerializeField] private int preferredResidents = 78;

        [SerializeField] private List<string> requiredBusinessIds = new List<string>();
        [SerializeField] private string populationNotes = string.Empty;
        [SerializeField] private string economicNotes = string.Empty;
        [SerializeField] private string propertyNotes = string.Empty;

        public string ScenarioId => scenarioId;
        public string DefinitionVersion => definitionVersion;
        public int StartYear => startYear;
        public int StartMonth => startMonth;
        public int StartDay => startDay;
        public int MinResidents => minResidents;
        public int MaxResidents => maxResidents;
        public int PreferredResidents => preferredResidents;
        public IReadOnlyList<string> RequiredBusinessIds => requiredBusinessIds;
        public string PopulationNotes => populationNotes;
        public string EconomicNotes => economicNotes;
        public string PropertyNotes => propertyNotes;
    }

    /// <summary>
    /// Pass V P1-1 contract: a durable, versioned, reusable scenario preset.
    /// Supports load, validation, versioning, reuse, copy/derive, deterministic
    /// preview generation, diagnostics, and backward compatibility.
    /// </summary>
    [Serializable]
    public sealed class ScenarioPreset
    {
        [SerializeField] private string presetId = string.Empty;
        [SerializeField] private string presetName = string.Empty;
        [SerializeField] private string presetVersion = "1.0.0";
        [SerializeField] private string derivedFromPresetId = string.Empty;
        [SerializeField] private StartingWorldConfiguration startingWorld = new StartingWorldConfiguration();
        [SerializeField] private List<string> diagnostics = new List<string>();

        public string PresetId => presetId;
        public string PresetName => presetName;
        public string PresetVersion => presetVersion;
        public string DerivedFromPresetId => derivedFromPresetId;
        public StartingWorldConfiguration StartingWorld => startingWorld;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public void AddDiagnostic(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                diagnostics.Add(message);
            }
        }
    }

    /// <summary>
    /// Pass V P1-1 contract: ONE authoritative scenario coordinator. It manages
    /// scenario identity, configuration, generation, initialization, validation, and
    /// lifecycle. It commands existing initialization services and retains scenario
    /// metadata only. It stores no Person, Household, Business, inventory, property,
    /// or money state.
    /// </summary>
    public interface IScenarioCoordinator
    {
        string ActiveScenarioId { get; }
        string ActivePresetId { get; }
        int ActiveSeed { get; }

        bool RegisterPreset(ScenarioPreset preset, List<string> diagnostics);
        ScenarioPreset GetPreset(string presetId);
        bool ValidatePreset(string presetId, List<string> diagnostics);

        /// <summary>
        /// Executes a lifecycle action with the given seed. Returns false with
        /// diagnostics when generation or validation fails; never invents
        /// money, houses, or businesses to force validity.
        /// </summary>
        bool ExecuteLifecycle(ScenarioLifecycleAction action, int seed, List<string> diagnostics);

        ScenarioPreset CapturePresetDto();
        bool RestorePresetDto(ScenarioPreset dto, List<string> diagnostics);
    }

    /// <summary>
    /// Pass V P1-1 contract: documents what each lifecycle action must and must
    /// not do, so New Game / Load / Restart / Replay can never be confused.
    /// </summary>
    public static class ScenarioLifecycleSemantics
    {
        /// <summary>
        /// State that a genuine New Game must never inherit from an earlier run.
        /// Verified by ghost-state regression tests.
        /// </summary>
        public static readonly string[] NewGameMustNotInherit =
        {
            "player-ownership",
            "household-memberships",
            "employment",
            "scenario-objectives",
            "manager-decisions",
            "generated-businesses",
            "banking-state",
            "stale-reservations",
        };
    }
}
