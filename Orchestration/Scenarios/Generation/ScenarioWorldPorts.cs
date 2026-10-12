using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): outcome of one world-generation attempt. Carries the
    /// world fingerprint and counts; the realized world itself lives in the
    /// domain authorities, never in the coordinator.
    /// </summary>
    [Serializable]
    public sealed class ScenarioGenerationOutcome
    {
        public bool Success;
        public string WorldFingerprint = string.Empty;
        public int ResidentCount;
        public int HouseholdCount;
        public int BusinessCount;
        public int AttemptUsed;
        public string Summary = string.Empty;
        public List<string> ProvenanceTags = new List<string>();
    }

    /// <summary>
    /// Pass V P1-1: generates one realized world from a preset + seed + streams.
    /// Implementations command the existing init services (PopulationGenerator in
    /// production; the deterministic ScenarioWorldBuilder for previews/tests).
    /// Must never invent money, houses, or businesses to force validity: on
    /// constraint violation it reports failure with actionable diagnostics.
    /// </summary>
    public interface IScenarioGenerationPort
    {
        /// <summary>How many generation attempts have been executed (observability).</summary>
        int GenerationCalls { get; }

        ScenarioGenerationOutcome Generate(
            ScenarioPreset preset,
            int seed,
            int startDayIndex,
            GenerationRngStreams rng,
            List<string> diagnostics);
    }

    /// <summary>
    /// Pass V P1-5: purges all run-scoped state before a New Game / Restart /
    /// Replay so the fresh world cannot inherit ghost state. Implementations
    /// clear each category through its owning authority.
    /// </summary>
    public interface IScenarioWorldResetPort
    {
        IReadOnlyList<string> ClearedCategories { get; }
        void ResetForNewGame(List<string> diagnostics);
    }

    /// <summary>
    /// Pass V P1-5: persists the campaign record (metadata only). Load restores
    /// the record; it never regenerates the world.
    /// </summary>
    public interface IScenarioCampaignStore
    {
        bool TryLoad(string slotId, out ScenarioCampaignRecord record);
        void Save(string slotId, ScenarioCampaignRecord record);
        void Delete(string slotId);
    }

    /// <summary>
    /// Pass V P1-5: the domain authorities the starting-economy seeder writes
    /// through. ONE cash writer (HouseholdLedgerRegistry), ONE debt authority
    /// (FinancialObligationAuthority), ONE employment registry. The seeder never
    /// invents parallel books.
    /// </summary>
    public sealed class ScenarioEconomyAuthorityPorts
    {
        public HouseholdLedgerRegistry Ledgers { get; }
        public FinancialObligationAuthority Obligations { get; }
        public EmploymentRelationshipRegistry Employments { get; }
        public EntityIdRegistry Ids { get; }

        public ScenarioEconomyAuthorityPorts(
            HouseholdLedgerRegistry ledgers,
            FinancialObligationAuthority obligations,
            EmploymentRelationshipRegistry employments,
            EntityIdRegistry ids)
        {
            Ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
            Obligations = obligations ?? throw new ArgumentNullException(nameof(obligations));
            Employments = employments ?? throw new ArgumentNullException(nameof(employments));
            Ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }
    }

    /// <summary>
    /// Pass V P1-5: authored business inventory grants are applied through real
    /// inventory positions with scenario provenance. No synthetic inventory:
    /// every grant names a real business and product.
    /// </summary>
    public interface IScenarioInventoryPort
    {
        LandLedgers.Economy.Generic.GenericInventoryPosition GetOrCreatePosition(string businessId, string productId);
    }

    /// <summary>
    /// Pass V P1-5: authored property grants. The harness/test implementation
    /// records them as data; the production implementation applies them through
    /// the title authority with scenario provenance.
    /// </summary>
    public interface IScenarioPropertyPort
    {
        void RecordGrant(ScenarioSeedPropertyGrant grant, string provenanceTag);
        IReadOnlyList<ScenarioSeedPropertyGrant> RecordedGrants { get; }
    }
}
