using System;
using System.Text;
using UnityEngine;

namespace LandLedgers.World
{
    public readonly struct WorldSeedResolution
    {
        public WorldSeedResolution(
            WorldGenerationStartupMode startupMode,
            string seedSource,
            int seed,
            string profileName,
            bool usedFallback,
            string warning)
        {
            StartupMode = startupMode;
            SeedSource = seedSource ?? string.Empty;
            Seed = Mathf.Max(0, seed);
            ProfileName = profileName ?? string.Empty;
            UsedFallback = usedFallback;
            Warning = warning ?? string.Empty;
        }

        public WorldGenerationStartupMode StartupMode { get; }
        public string SeedSource { get; }
        public int Seed { get; }
        public string ProfileName { get; }
        public bool UsedFallback { get; }
        public string Warning { get; }

        public string BuildLog()
        {
            StringBuilder builder = new();
            builder.Append("World Startup Mode: ");
            builder.Append(WorldSeedAuthority.FormatStartupMode(StartupMode));

            if (!string.IsNullOrWhiteSpace(ProfileName))
            {
                builder.AppendLine();
                builder.Append("Testing Profile: ");
                builder.Append(ProfileName);
            }

            builder.AppendLine();
            builder.Append("World Seed Source: ");
            builder.Append(SeedSource);
            builder.AppendLine();
            builder.Append("World Seed: ");
            builder.Append(Seed);
            return builder.ToString();
        }
    }

    public static class WorldSeedAuthority
    {
        public const string AcquisitionMarketSeedCategory = "AcquisitionMarket";

        public static WorldSeedResolution ResolveFreshGenerationSeed(
            TownGenerationSettings settings,
            bool runtimeFreshGeneration,
            int randomCandidateSeed)
        {
            if (settings == null)
            {
                return new WorldSeedResolution(
                    WorldGenerationStartupMode.RandomFreshRuntimeWorld,
                    "Missing Settings",
                    0,
                    string.Empty,
                    true,
                    "Town generation settings are missing; no world seed could be resolved.");
            }

            if (!runtimeFreshGeneration)
            {
                return new WorldSeedResolution(
                    settings.startupMode,
                    "Serialized Settings",
                    Mathf.Max(0, settings.seed),
                    ResolveProfileName(settings),
                    false,
                    string.Empty);
            }

            if (settings.useManualSeedOverride)
            {
                return ResolveManualSeed(settings);
            }

            return settings.startupMode switch
            {
                WorldGenerationStartupMode.FixedTestingWorld => ResolveFixedTestingSeed(settings),
                _ => ResolveRandomFreshSeed(settings, randomCandidateSeed)
            };
        }

        public static WorldSeedResolution ResolveSaveDataSeed(int savedSeed)
        {
            return new WorldSeedResolution(
                WorldGenerationStartupMode.RandomFreshRuntimeWorld,
                "Save Data",
                Mathf.Max(0, savedSeed),
                string.Empty,
                false,
                string.Empty);
        }

        public static int DeriveChildSeed(int masterSeed, string category)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)Mathf.Max(0, masterSeed)) * 16777619u;

                string value = string.IsNullOrWhiteSpace(category) ? "Uncategorized" : category.Trim();
                for (int i = 0; i < value.Length; i++)
                {
                    hash = (hash ^ value[i]) * 16777619u;
                }

                int seed = (int)(hash & 0x7fffffff);
                return seed > 0 ? seed : 1;
            }
        }

        public static string FormatStartupMode(WorldGenerationStartupMode startupMode)
        {
            return startupMode switch
            {
                WorldGenerationStartupMode.FixedTestingWorld => "Fixed Testing World",
                _ => "Random Fresh Runtime World"
            };
        }

        private static WorldSeedResolution ResolveManualSeed(TownGenerationSettings settings)
        {
            if (settings.manualSeedOverride > 0)
            {
                return new WorldSeedResolution(
                    settings.startupMode,
                    "Manual Seed Override",
                    settings.manualSeedOverride,
                    ResolveProfileName(settings),
                    false,
                    string.Empty);
            }

            int fallback = ResolveDeterministicFallbackSeed(settings);
            return new WorldSeedResolution(
                settings.startupMode,
                "Manual Seed Override Fallback",
                fallback,
                ResolveProfileName(settings),
                true,
                "Manual seed override is enabled but has no positive seed; using deterministic fallback seed.");
        }

        private static WorldSeedResolution ResolveFixedTestingSeed(TownGenerationSettings settings)
        {
            string profileName = ResolveProfileName(settings);
            if (settings.fixedTestingWorldSeed > 0)
            {
                return new WorldSeedResolution(
                    WorldGenerationStartupMode.FixedTestingWorld,
                    "Fixed Test Profile",
                    settings.fixedTestingWorldSeed,
                    profileName,
                    false,
                    string.Empty);
            }

            int fallback = ResolveDeterministicFallbackSeed(settings);
            return new WorldSeedResolution(
                WorldGenerationStartupMode.FixedTestingWorld,
                "Fixed Test Profile Fallback",
                fallback,
                profileName,
                true,
                $"Fixed testing profile '{profileName}' has no positive seed; using deterministic fallback seed.");
        }

        private static WorldSeedResolution ResolveRandomFreshSeed(TownGenerationSettings settings, int randomCandidateSeed)
        {
            int seed = randomCandidateSeed > 0 ? randomCandidateSeed : 1;
            string warning = randomCandidateSeed > 0
                ? string.Empty
                : "Random fresh runtime world received an invalid random candidate seed; using deterministic fallback seed 1.";

            return new WorldSeedResolution(
                WorldGenerationStartupMode.RandomFreshRuntimeWorld,
                randomCandidateSeed > 0 ? "Generated Random Seed" : "Generated Random Seed Fallback",
                seed,
                string.Empty,
                randomCandidateSeed <= 0,
                warning);
        }

        private static int ResolveDeterministicFallbackSeed(TownGenerationSettings settings)
        {
            return settings != null && settings.seed > 0 ? settings.seed : 1;
        }

        private static string ResolveProfileName(TownGenerationSettings settings)
        {
            if (settings == null || settings.startupMode != WorldGenerationStartupMode.FixedTestingWorld)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(settings.fixedTestingProfileName)
                ? "Default QA Town"
                : settings.fixedTestingProfileName.Trim();
        }
    }
}
