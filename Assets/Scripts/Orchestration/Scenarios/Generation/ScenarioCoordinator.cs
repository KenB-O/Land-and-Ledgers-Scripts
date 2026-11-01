using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1/P1-5 (Worker 1): ONE authoritative scenario coordinator. It
    /// manages scenario identity, configuration, generation, initialization,
    /// validation, and lifecycle. It commands the injected ports (generation,
    /// reset, campaign store, economy authorities) and retains scenario
    /// metadata only — it stores no Person, Household, Business, inventory,
    /// property, or money state.
    ///
    /// Lifecycle semantics (see ScenarioLifecycleSemantics):
    /// - NewGame: purge ghost state, validate, generate from preset + seed,
    ///   record the campaign. Never inherits prior-run state.
    /// - LoadGame: restore the saved campaign record; never regenerates.
    /// - RestartSameSeed: purge + regenerate deterministically from the same seed.
    /// - RestartNewSeed: purge + regenerate from a new seed.
    /// - ReplayFromPreset: fresh campaign from the selected preset.
    /// - ContinueFailed: preserve the current world; never secretly regenerate.
    /// - Edit preset (UpdatePreset): version-bumps the registry entry; the active
    ///   campaign keeps its recorded definition version, so edits affect future
    ///   generations only.
    ///
    /// Generation failures retry within a bounded policy (partitioned streams)
    /// or report actionably; money, houses, and businesses are never invented
    /// to force validity.
    /// </summary>
    public sealed class ScenarioCoordinator : IScenarioCoordinator
    {
        public const string DefaultCampaignSlot = "default";

        private readonly Dictionary<string, ScenarioPreset> presets =
            new Dictionary<string, ScenarioPreset>(StringComparer.Ordinal);

        private readonly IScenarioGenerationPort generationPort;
        private readonly IScenarioWorldResetPort resetPort;
        private readonly IScenarioCampaignStore campaignStore;
        private readonly List<string> lifecycleLog = new List<string>();

        private ScenarioCampaignRecord activeCampaign;

        /// <summary>
        /// The preset id used by NewGame and ReplayFromPreset. Metadata only;
        /// set by the start-screen or test before executing the lifecycle.
        /// </summary>
        public string SelectedPresetId { get; set; } = string.Empty;

        public string CampaignSlotId { get; set; } = DefaultCampaignSlot;

        public ScenarioCoordinator(
            IScenarioGenerationPort generationPort,
            IScenarioWorldResetPort resetPort,
            IScenarioCampaignStore campaignStore)
        {
            this.generationPort = generationPort ?? throw new ArgumentNullException(nameof(generationPort));
            this.resetPort = resetPort ?? throw new ArgumentNullException(nameof(resetPort));
            this.campaignStore = campaignStore ?? throw new ArgumentNullException(nameof(campaignStore));
        }

        public string ActiveScenarioId => activeCampaign?.ScenarioId ?? string.Empty;
        public string ActivePresetId => activeCampaign?.PresetId ?? string.Empty;
        public int ActiveSeed => activeCampaign?.Seed ?? 0;

        /// <summary>Metadata copy of the active campaign record (no world state).</summary>
        public ScenarioCampaignRecord ActiveCampaign => activeCampaign?.Copy();

        public IReadOnlyList<string> LifecycleLog => lifecycleLog;

        public IReadOnlyList<string> RegisteredPresetIds
        {
            get
            {
                var ids = new List<string>(presets.Keys);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        // ---------- preset registry (contract) ----------

        public bool RegisterPreset(ScenarioPreset preset, List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (preset == null)
            {
                diagnostics.Add("Cannot register a null preset.");
                return false;
            }

            if (!ScenarioPresetLibrary.Validate(preset, diagnostics))
            {
                diagnostics.Add($"Preset '{preset.PresetId}' was NOT registered: validation failed.");
                return false;
            }

            if (presets.ContainsKey(preset.PresetId))
            {
                diagnostics.Add($"Duplicate preset id '{preset.PresetId}': first registration wins.");
                return false;
            }

            presets[preset.PresetId] = ScenarioPresetLibrary.DeepCopy(preset);
            diagnostics.Add($"Preset '{preset.PresetId}' v{preset.PresetVersion} registered.");
            return true;
        }

        public ScenarioPreset GetPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return null;
            }

            return presets.TryGetValue(presetId, out ScenarioPreset preset) ? preset : null;
        }

        public bool ValidatePreset(string presetId, List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            ScenarioPreset preset = GetPreset(presetId);
            if (preset == null)
            {
                diagnostics.Add($"Preset '{presetId}' is not registered.");
                return false;
            }

            return ScenarioPresetLibrary.Validate(preset, diagnostics);
        }

        /// <summary>
        /// Edit-preset semantics: replaces the registry entry with a version
        /// bump. The active campaign keeps its recorded definition version, so
        /// this affects future generations only — never the live world.
        /// </summary>
        public bool UpdatePreset(ScenarioPreset edited, List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (edited == null || string.IsNullOrWhiteSpace(edited.PresetId))
            {
                diagnostics.Add("Cannot update: preset or preset id is missing.");
                return false;
            }

            if (!presets.ContainsKey(edited.PresetId))
            {
                diagnostics.Add($"Cannot update: preset '{edited.PresetId}' is not registered.");
                return false;
            }

            if (!ScenarioPresetLibrary.Validate(edited, diagnostics))
            {
                diagnostics.Add($"Preset '{edited.PresetId}' was NOT updated: validation failed.");
                return false;
            }

            ScenarioPreset prior = presets[edited.PresetId];
            string bumped = BumpMinorVersion(prior.PresetVersion);
            edited.SetIdentity(edited.PresetId, edited.PresetName, bumped);
            presets[edited.PresetId] = ScenarioPresetLibrary.DeepCopy(edited);

            diagnostics.Add($"Preset '{edited.PresetId}' updated to v{bumped}. " +
                "Applies to future generations only; the active campaign keeps its recorded definition version.");
            if (activeCampaign != null && string.Equals(activeCampaign.PresetId, edited.PresetId, StringComparison.Ordinal))
            {
                diagnostics.Add($"Active campaign still runs definition v{activeCampaign.DefinitionVersion} " +
                    "(Edit Preset never mutates the live world).");
            }

            return true;
        }

        // ---------- lifecycle (contract) ----------

        public bool ExecuteLifecycle(ScenarioLifecycleAction action, int seed, List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            switch (action)
            {
                case ScenarioLifecycleAction.NewGame:
                    return ExecuteNewGame(SelectedPresetId, seed, "NewGame", diagnostics);
                case ScenarioLifecycleAction.ReplayFromPreset:
                    return ExecuteNewGame(SelectedPresetId, seed, "ReplayFromPreset", diagnostics);
                case ScenarioLifecycleAction.RestartSameSeed:
                    return ExecuteRestart(null, diagnostics);
                case ScenarioLifecycleAction.RestartNewSeed:
                    return ExecuteRestart(seed, diagnostics);
                case ScenarioLifecycleAction.LoadGame:
                    return ExecuteLoad(diagnostics);
                case ScenarioLifecycleAction.ContinueFailed:
                    return ExecuteContinueFailed(diagnostics);
                default:
                    diagnostics.Add($"Unknown lifecycle action {(int)action}; nothing was done.");
                    return false;
            }
        }

        public ScenarioPreset CapturePresetDto()
        {
            return ScenarioPresetLibrary.DeepCopy(GetPreset(ActivePresetId));
        }

        public bool RestorePresetDto(ScenarioPreset dto, List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.PresetId))
            {
                diagnostics.Add("Cannot restore a null preset DTO.");
                return false;
            }

            if (!ScenarioPresetLibrary.Validate(dto, diagnostics))
            {
                diagnostics.Add($"Preset DTO '{dto.PresetId}' failed validation; not restored.");
                return false;
            }

            presets[dto.PresetId] = ScenarioPresetLibrary.DeepCopy(dto);
            diagnostics.Add($"Preset DTO '{dto.PresetId}' restored to the registry.");
            return true;
        }

        // ---------- lifecycle internals ----------

        private bool ExecuteNewGame(string presetId, int seed, string actionName, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                diagnostics.Add($"{actionName}: no preset selected (SelectedPresetId is empty). Nothing was generated.");
                return false;
            }

            ScenarioPreset preset = GetPreset(presetId);
            if (preset == null)
            {
                diagnostics.Add($"{actionName}: preset '{presetId}' is not registered. Nothing was generated.");
                return false;
            }

            // Ghost-state prevention FIRST: the fresh world must not inherit
            // player ownership, memberships, employment, objectives, manager
            // decisions, businesses, banking state, or stale reservations.
            resetPort.ResetForNewGame(diagnostics);
            diagnostics.Add($"{actionName}: world reset cleared: {string.Join(", ", resetPort.ClearedCategories)}.");

            if (!ScenarioPresetLibrary.Validate(preset, diagnostics))
            {
                diagnostics.Add($"{actionName}: preset '{presetId}' failed validation; world left empty, nothing invented.");
                return false;
            }

            StartingWorldConfiguration world = preset.StartingWorld;
            int startDayIndex = ScenarioDateUtil.ToDayIndex(world.StartYear, world.StartMonth, world.StartDay);
            var rng = new GenerationRngStreams(world.DefinitionVersion, seed, startDayIndex);

            ScenarioGenerationOutcome outcome = generationPort.Generate(preset, seed, startDayIndex, rng, diagnostics);
            if (outcome == null || !outcome.Success)
            {
                diagnostics.Add($"{actionName}: generation failed for preset '{presetId}' seed {seed}. " +
                    "The world was left empty; no money, houses, or businesses were invented to force validity.");
                return false;
            }

            string postViolation = ValidateOutcome(outcome, world);
            if (postViolation != null)
            {
                diagnostics.Add($"{actionName}: generated world rejected: {postViolation}. " +
                    "The world was left empty; nothing was invented to force validity.");
                return false;
            }

            activeCampaign = ScenarioCampaignRecord.Create(preset, seed, startDayIndex, outcome.WorldFingerprint);
            activeCampaign.NoteLifecycle($"{actionName} seed={seed} fingerprint={outcome.WorldFingerprint}");
            campaignStore.Save(CampaignSlotId, activeCampaign.Copy());

            diagnostics.Add($"{actionName}: campaign started from preset '{presetId}' " +
                $"(definition v{world.DefinitionVersion}) seed {seed}, start {activeCampaign.StartDateIso}, " +
                $"fingerprint {outcome.WorldFingerprint}. {outcome.Summary}");
            lifecycleLog.Add($"{actionName} preset={presetId} seed={seed} fingerprint={outcome.WorldFingerprint}");
            return true;
        }

        private bool ExecuteRestart(int? seedOverride, List<string> diagnostics)
        {
            if (activeCampaign == null)
            {
                diagnostics.Add("Restart: no active campaign to restart. Start a New Game first.");
                return false;
            }

            int seed = seedOverride ?? activeCampaign.Seed;
            string actionName = seedOverride.HasValue ? "RestartNewSeed" : "RestartSameSeed";
            return ExecuteNewGame(activeCampaign.PresetId, seed, actionName, diagnostics);
        }

        private bool ExecuteLoad(List<string> diagnostics)
        {
            if (!campaignStore.TryLoad(CampaignSlotId, out ScenarioCampaignRecord record) || record == null)
            {
                diagnostics.Add($"LoadGame: no saved campaign in slot '{CampaignSlotId}'. Nothing was regenerated.");
                return false;
            }

            // Load restores the record; the world itself is restored by the
            // save pipeline (clear-then-load). Generation is NEVER invoked here.
            activeCampaign = record.Copy();
            activeCampaign.NoteLifecycle("LoadGame: campaign record restored; world restored from save, not regenerated.");
            diagnostics.Add($"LoadGame: restored campaign '{record.PresetId}' seed {record.Seed} " +
                $"fingerprint {record.GenerationFingerprint}. No regeneration performed.");
            lifecycleLog.Add($"LoadGame preset={record.PresetId} seed={record.Seed} fingerprint={record.GenerationFingerprint}");
            return true;
        }

        private bool ExecuteContinueFailed(List<string> diagnostics)
        {
            if (activeCampaign == null)
            {
                diagnostics.Add("ContinueFailed: no active campaign to preserve.");
                return false;
            }

            activeCampaign.NoteLifecycle("ContinueFailed: world preserved; nothing regenerated, nothing reset.");
            diagnostics.Add($"ContinueFailed: preserved campaign '{activeCampaign.PresetId}' seed {activeCampaign.Seed}. " +
                "The world was left exactly as it was.");
            lifecycleLog.Add($"ContinueFailed preset={activeCampaign.PresetId} seed={activeCampaign.Seed} (preserved)");
            return true;
        }

        private static string ValidateOutcome(ScenarioGenerationOutcome outcome, StartingWorldConfiguration world)
        {
            if (outcome.ResidentCount < world.MinResidents || outcome.ResidentCount > world.MaxResidents)
            {
                return $"resident count {outcome.ResidentCount} outside authored range " +
                    $"[{world.MinResidents},{world.MaxResidents}]";
            }

            if (outcome.ResidentCount < ScenarioPresetLibrary.CanonMinResidents
                || outcome.ResidentCount > ScenarioPresetLibrary.CanonMaxResidents)
            {
                return $"resident count {outcome.ResidentCount} outside the Canon calibration " +
                    $"[{ScenarioPresetLibrary.CanonMinResidents},{ScenarioPresetLibrary.CanonMaxResidents}]";
            }

            if (string.IsNullOrWhiteSpace(outcome.WorldFingerprint))
            {
                return "world fingerprint is missing";
            }

            return null;
        }

        private static string BumpMinorVersion(string version)
        {
            int[] parts = { 1, 0, 0 };
            if (!string.IsNullOrWhiteSpace(version))
            {
                string[] tokens = version.Split('.');
                for (int i = 0; i < Math.Min(3, tokens.Length); i++)
                {
                    int.TryParse(tokens[i], out parts[i]);
                }
            }

            parts[1]++;
            parts[2] = 0;
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        }
    }
}
