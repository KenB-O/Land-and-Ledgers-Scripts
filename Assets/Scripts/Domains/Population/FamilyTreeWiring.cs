using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 4 (finding F01): the tested seam for the one wiring
    /// call FamilyTreeBuilder still needs. FamilyTreeBuilder itself is
    /// deliberately NOT called by PopulationGenerator: calling it inside
    /// generation would change the on-map people count in existing
    /// generation tests, because off-map records share
    /// PopulationState.people. The production wiring point is exactly
    /// one call, after generation and before the first save:
    ///
    ///   FamilyTreeWiring.ApplyAfterGeneration(population, kinship,
    ///       worldSeed, scenarioStartYear, diagnostics);
    ///
    /// Codex call site: FirstLedgerSliceBootstrapper (or the equivalent
    /// New Game path in Main Scene), immediately after
    /// PopulationManager.GeneratePopulationSnapshot returns, before the
    /// first save. Arguments: the PopulationManager's live PopulationState
    /// and KinshipRegistry, the world seed, and the scenario start year
    /// from the calendar anchor (the reference year only anchors derived
    /// birth dates; it is NOT canon). The call is idempotent: a second
    /// call creates no new parents, grandparents, or sibling edges, and
    /// off-map relatives created by the first call are never re-processed
    /// as residents. Household membership joining on materialisation
    /// still belongs to ArrivalSettlementDriver, not to this seam.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public static class FamilyTreeWiring
    {
        public static FamilyTreeBuilder.Report ApplyAfterGeneration(
            PopulationState population,
            KinshipRegistry kinship,
            int seed,
            int referenceYear,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            FamilyTreeBuilder.Report report = FamilyTreeBuilder.EnsureCompleteTree(
                population, kinship, seed, referenceYear, diagnostics);
            diagnostics.Add(
                $"FamilyTreeWiring: applied after generation — {report.PersonsProcessed} residents processed, " +
                $"{report.ParentsCreated} parents, {report.GrandparentsCreated} grandparents, " +
                $"{report.SiblingEdgesAdded} sibling edges added.");
            return report;
        }
    }
}
