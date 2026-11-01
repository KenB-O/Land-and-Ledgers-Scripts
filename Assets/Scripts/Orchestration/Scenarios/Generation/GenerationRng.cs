using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): partitioned generation concerns. Each concern draws
    /// from its own stream, so tuning one concern (e.g. debt rolls) can never
    /// change another concern's sequence (e.g. household composition).
    /// APPEND-ONLY: never renumber or remove entries.
    /// </summary>
    public enum GenerationConcern
    {
        Population = 0,
        HouseholdComposition = 1,
        Employment = 2,
        StartingCash = 3,
        Debt = 4,
        Property = 5,
        Inventory = 6,
        BusinessFormation = 7,
        PlayerStart = 8,
        HistoryEvents = 9,
    }

    /// <summary>
    /// Pass V P1-1 (Worker 1): deterministic partitioned RNG. The same
    /// (definitionVersion, seed, startDayIndex, attempt) always yields the same
    /// per-concern streams, so the same preset + seed + start date + inputs
    /// generates an identical world. Attempts are partitioned too: a bounded
    /// retry uses a different attempt index rather than perturbing other streams.
    /// </summary>
    public sealed class GenerationRngStreams
    {
        public const int MaxAttempts = 8;

        private readonly Dictionary<GenerationConcern, Random> streams =
            new Dictionary<GenerationConcern, Random>();

        public string DefinitionVersion { get; }
        public int Seed { get; }
        public int StartDayIndex { get; }
        public int Attempt { get; }

        public GenerationRngStreams(string definitionVersion, int seed, int startDayIndex, int attempt = 0)
        {
            DefinitionVersion = definitionVersion ?? string.Empty;
            Seed = seed;
            StartDayIndex = startDayIndex;
            Attempt = Math.Max(0, attempt);

            foreach (GenerationConcern concern in (GenerationConcern[])Enum.GetValues(typeof(GenerationConcern)))
            {
                streams[concern] = new Random(DeriveStreamSeed(DefinitionVersion, Seed, StartDayIndex, Attempt, concern));
            }
        }

        public Random Stream(GenerationConcern concern)
        {
            return streams[concern];
        }

        /// <summary>Streams for the next bounded retry attempt.</summary>
        public GenerationRngStreams NextAttempt()
        {
            return new GenerationRngStreams(DefinitionVersion, Seed, StartDayIndex, Attempt + 1);
        }

        internal static int DeriveStreamSeed(string definitionVersion, int seed, int startDayIndex, int attempt, GenerationConcern concern)
        {
            unchecked
            {
                // FNV-1a 32-bit over the canonical input string. Stable across
                // runs and platforms for identical inputs (ASCII digits only).
                uint hash = 2166136261u;
                string canonical = string.Concat(
                    definitionVersion, "|", seed.ToString(), "|",
                    startDayIndex.ToString(), "|", attempt.ToString(), "|", concern.ToString());
                for (int i = 0; i < canonical.Length; i++)
                {
                    hash ^= canonical[i];
                    hash *= 16777619u;
                }

                return (int)(hash & 0x7FFFFFFFu);
            }
        }
    }
}
