using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): the durable record of one realized campaign. This is
    /// the only state the ScenarioCoordinator keeps about a live world: scenario
    /// identity, preset identity + definition version, seed, start date, and the
    /// generation fingerprint. It stores no Person, Household, Business, inventory,
    /// property, or money data — those live in their own authorities.
    /// Serializable with plain public fields for the JSON save pipeline.
    /// </summary>
    [Serializable]
    public sealed class ScenarioCampaignRecord
    {
        public string ScenarioId = string.Empty;
        public string PresetId = string.Empty;
        public string DefinitionVersion = string.Empty;
        public int Seed;
        public int StartDayIndex;
        public string StartDateIso = string.Empty;
        public string GenerationFingerprint = string.Empty;
        public string ProvenanceTag = string.Empty;
        public long StartedUnixSeconds;
        public List<string> LifecycleHistory = new List<string>();

        public static ScenarioCampaignRecord Create(
            ScenarioPreset preset,
            int seed,
            int startDayIndex,
            string generationFingerprint)
        {
            var record = new ScenarioCampaignRecord
            {
                ScenarioId = preset?.StartingWorld?.ScenarioId ?? string.Empty,
                PresetId = preset?.PresetId ?? string.Empty,
                DefinitionVersion = preset?.StartingWorld?.DefinitionVersion ?? string.Empty,
                Seed = seed,
                StartDayIndex = startDayIndex,
                GenerationFingerprint = generationFingerprint ?? string.Empty,
                StartedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            StartingWorldConfiguration world = preset?.StartingWorld;
            if (world != null)
            {
                record.StartDateIso = ScenarioDateUtil.ToIsoDate(world.StartYear, world.StartMonth, world.StartDay);
            }

            record.ProvenanceTag = BuildProvenanceTag(record.PresetId, record.DefinitionVersion, seed);
            return record;
        }

        public static string BuildProvenanceTag(string presetId, string definitionVersion, int seed)
        {
            return $"scenario:{presetId}@{definitionVersion}/seed:{seed}";
        }

        public void NoteLifecycle(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return;
            }

            LifecycleHistory.Add($"[{DateTimeOffset.UtcNow:O}] {entry}");
        }

        public ScenarioCampaignRecord Copy()
        {
            return new ScenarioCampaignRecord
            {
                ScenarioId = ScenarioId,
                PresetId = PresetId,
                DefinitionVersion = DefinitionVersion,
                Seed = Seed,
                StartDayIndex = StartDayIndex,
                StartDateIso = StartDateIso,
                GenerationFingerprint = GenerationFingerprint,
                ProvenanceTag = ProvenanceTag,
                StartedUnixSeconds = StartedUnixSeconds,
                LifecycleHistory = new List<string>(LifecycleHistory ?? new List<string>()),
            };
        }

        /// <summary>
        /// True when two records describe the same realized world (same preset
        /// definition version, seed, start date, and generation fingerprint).
        /// </summary>
        public bool DescribesSameWorld(ScenarioCampaignRecord other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(PresetId, other.PresetId, StringComparison.Ordinal)
                && string.Equals(DefinitionVersion, other.DefinitionVersion, StringComparison.Ordinal)
                && Seed == other.Seed
                && StartDayIndex == other.StartDayIndex
                && string.Equals(GenerationFingerprint, other.GenerationFingerprint, StringComparison.Ordinal);
        }
    }
}
