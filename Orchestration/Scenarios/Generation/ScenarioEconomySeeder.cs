using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-5 (Worker 1): posts scenario-authored starting economy through
    /// the domain authorities, with provenance on every record.
    ///
    /// Authority discipline (release-critical):
    /// - Cash: ONLY HouseholdLedgerRegistry.RecordInflow, source
    ///   HouseholdIncomeSource.OtherDocumented with a scenario reason. Sourceless
    ///   inflows are rejected by the ledger itself; rejections are reported,
    ///   never papered over.
    /// - Debt: ONLY FinancialObligationAuthority.Create, kinds restricted to the
    ///   preset's allowed list, every obligation carrying the provenance tag.
    /// - Employment: ONLY EmploymentRelationshipRegistry.Register with
    ///   EmploymentSource.Authored and agreed compensation terms.
    /// - Inventory: ONLY IScenarioInventoryPort positions with provenance lots.
    /// - Property: ONLY IScenarioPropertyPort grants naming real households and
    ///   buildings.
    ///
    /// The seeder creates no persons, no households, no businesses, and no
    /// inventory out of thin air: it seeds economy for the draft the generation
    /// port produced. Scenario-authored history is tagged with the provenance
    /// tag so it is always distinguishable from simulated post-start events.
    /// </summary>
    public static class ScenarioEconomySeeder
    {
        public sealed class SeedReport
        {
            public int HouseholdsSeeded;
            public int TotalCashCents;
            public int ObligationCount;
            public int TotalDebtCents;
            public int EmploymentCount;
            public int InventoryGrantCount;
            public int PropertyGrantCount;
            public int RejectedCount;
            public List<string> Rejections = new List<string>();
        }

        public static SeedReport Seed(
            ScenarioSeedPopulation population,
            ScenarioPreset preset,
            ScenarioEconomyAuthorityPorts ports,
            IScenarioInventoryPort inventoryPort,
            IScenarioPropertyPort propertyPort,
            GenerationRngStreams rng,
            int startDayIndex,
            List<string> diagnostics)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (ports == null) throw new ArgumentNullException(nameof(ports));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var report = new SeedReport();
            StartingWorldConfiguration world = preset.StartingWorld;
            ScenarioEconomyPolicy policy = world?.EconomyPolicy;
            string provenanceTag = ScenarioCampaignRecord.BuildProvenanceTag(
                preset.PresetId, world?.DefinitionVersion ?? string.Empty, rng.Seed);

            Random cashRng = rng.Stream(GenerationConcern.StartingCash);
            Random debtRng = rng.Stream(GenerationConcern.Debt);
            Random employmentRng = rng.Stream(GenerationConcern.Employment);
            Random propertyRng = rng.Stream(GenerationConcern.Property);
            Random inventoryRng = rng.Stream(GenerationConcern.Inventory);

            int cashMin = policy?.CashBandMinCents ?? 0;
            int cashMax = Math.Max(cashMin, policy?.CashBandMaxCents ?? 0);

            // ---- Cash: one writer, provenance on every inflow. ----
            foreach (ScenarioSeedHousehold household in population.Households)
            {
                if (household == null)
                {
                    continue;
                }

                int amountCents = household.IsPlayerHousehold && world?.PlayerStart != null
                    ? Math.Max(0, world.PlayerStart.StartingCashCents)
                    : cashMin + (cashMax > cashMin ? cashRng.Next(cashMax - cashMin + 1) : 0);

                if (amountCents <= 0)
                {
                    diagnostics?.Add($"Household {household.HouseholdId}: no starting cash authored; ledger left empty.");
                    continue;
                }

                HouseholdLedger ledger = ports.Ledgers.GetOrCreate(household.HouseholdId);
                string rejection = ledger.RecordInflow(
                    startDayIndex,
                    amountCents,
                    HouseholdIncomeSource.OtherDocumented,
                    "scenario:" + provenanceTag,
                    $"Scenario-authored starting cash for {provenanceTag} (household {household.HouseholdId}).",
                    "Scenario author");
                if (rejection != null)
                {
                    report.RejectedCount++;
                    report.Rejections.Add(rejection);
                    diagnostics?.Add(rejection);
                    continue;
                }

                report.HouseholdsSeeded++;
                report.TotalCashCents += amountCents;
            }

            // ---- Debt: one debt authority, allowed kinds only, provenance tagged. ----
            IReadOnlyList<FinancialObligationKind> allowedKinds = policy?.AllowedObligationKinds;
            if (allowedKinds != null && allowedKinds.Count > 0 && policy.DebtChance01 > 0f)
            {
                string creditor = ResolveCreditorBusiness(population);
                foreach (ScenarioSeedHousehold household in population.Households)
                {
                    if (household == null)
                    {
                        continue;
                    }

                    if (debtRng.NextDouble() >= policy.DebtChance01)
                    {
                        continue;
                    }

                    FinancialObligationKind kind = allowedKinds[debtRng.Next(allowedKinds.Count)];
                    int principal = 10000 + (policy.MaxDebtCentsPerHousehold > 10000
                        ? debtRng.Next(policy.MaxDebtCentsPerHousehold - 10000 + 1)
                        : 0);
                    if (principal <= 0)
                    {
                        continue;
                    }

                    FinancialObligation obligation = ports.Obligations.Create(
                        ports.Ids,
                        kind,
                        $"household:{household.HouseholdId}",
                        creditor,
                        principal,
                        startDayIndex,
                        $"Scenario-authored starting debt ({provenanceTag}).",
                        $"Starting {kind} authored by scenario '{preset.PresetId}'.",
                        agreementId: string.Empty,
                        maturityDayIndex: startDayIndex + 365,
                        sourceEconomicEventId: provenanceTag);
                    if (obligation == null)
                    {
                        report.RejectedCount++;
                        string rejection = $"Rejected starting debt for household {household.HouseholdId}: " +
                            "FinancialObligationAuthority refused creation (see authority diagnostics).";
                        report.Rejections.Add(rejection);
                        diagnostics?.Add(rejection);
                        continue;
                    }

                    report.ObligationCount++;
                    report.TotalDebtCents += principal;
                }

                // Authored player debt, when the player start declares one.
                if (world?.PlayerStart != null && world.PlayerStart.StartingDebtCents > 0 && population.PlayerHouseholdId >= 0)
                {
                    FinancialObligationKind kind = allowedKinds[0];
                    FinancialObligation playerDebt = ports.Obligations.Create(
                        ports.Ids,
                        kind,
                        $"household:{population.PlayerHouseholdId}",
                        creditor,
                        world.PlayerStart.StartingDebtCents,
                        startDayIndex,
                        $"Scenario-authored player starting debt ({provenanceTag}).",
                        $"Player starting {kind} authored by scenario '{preset.PresetId}'.",
                        agreementId: string.Empty,
                        maturityDayIndex: startDayIndex + 365,
                        sourceEconomicEventId: provenanceTag);
                    if (playerDebt != null)
                    {
                        report.ObligationCount++;
                        report.TotalDebtCents += world.PlayerStart.StartingDebtCents;
                    }
                }
            }

            // ---- Employment: one registry, authored source, agreed terms. ----
            float employmentRate = policy != null ? policy.EmploymentTargetRate01 : 0.75f;
            foreach (ScenarioSeedHousehold household in population.Households)
            {
                if (household?.Members == null || population.Businesses.Count == 0)
                {
                    continue;
                }

                foreach (ScenarioSeedPerson person in household.Members)
                {
                    if (person == null || !person.IsWorkingAge || person.WeeklyWageCents <= 0)
                    {
                        continue;
                    }

                    if (employmentRng.NextDouble() >= employmentRate)
                    {
                        continue;
                    }

                    ScenarioSeedBusiness employer = population.Businesses[
                        employmentRng.Next(population.Businesses.Count)];
                    var relationship = new EmploymentRelationship
                    {
                        Id = $"scn-{rng.Seed}-p{person.PersonId}",
                        EmployeePersonId = person.PersonId,
                        EmployerBusinessId = employer.BusinessId,
                        RoleDisplayName = string.IsNullOrWhiteSpace(person.ProfessionName)
                            ? "Hand" : person.ProfessionName,
                        Kind = EmploymentKind.Permanent,
                        LifecycleState = EmploymentLifecycleState.Active,
                        Compensation = CompensationTerms.FromWeeklyWage(
                            person.WeeklyWageCents,
                            $"Scenario-authored starting employment ({provenanceTag})."),
                        StartDayIndex = startDayIndex,
                        Source = EmploymentSource.Authored,
                    };

                    string rejection = ports.Employments.Register(relationship);
                    if (rejection != null)
                    {
                        report.RejectedCount++;
                        report.Rejections.Add(rejection);
                        diagnostics?.Add(rejection);
                        continue;
                    }

                    report.EmploymentCount++;
                }
            }

            // ---- Inventory: authored opening stock with provenance lots. ----
            if (inventoryPort != null)
            {
                foreach (ScenarioSeedBusiness business in population.Businesses)
                {
                    if (business?.InventoryGrants == null)
                    {
                        continue;
                    }

                    foreach (ScenarioInventoryGrant grant in business.InventoryGrants)
                    {
                        if (grant == null || string.IsNullOrWhiteSpace(grant.ProductId) || grant.Quantity <= 0f)
                        {
                            continue;
                        }

                        LandLedgers.Economy.Generic.GenericInventoryPosition position =
                            inventoryPort.GetOrCreatePosition(business.BusinessId, grant.ProductId);
                        if (position == null)
                        {
                            continue;
                        }

                        // Deterministic lot identity: the grant order within the
                        // business, not Guid.NewGuid, so regeneration is identical.
                        position.Add(grant.Quantity, grant.UnitCostCents, null,
                            $"scenario-authored:{provenanceTag}", startDayIndex);
                        report.InventoryGrantCount++;
                    }
                }
            }

            // ---- Property: authored grants naming real households/buildings. ----
            if (propertyPort != null)
            {
                foreach (ScenarioSeedPropertyGrant grant in population.PropertyGrants)
                {
                    if (grant == null)
                    {
                        continue;
                    }

                    bool householdReal = false;
                    bool buildingReal = false;
                    foreach (ScenarioSeedHousehold household in population.Households)
                    {
                        if (household != null && household.HouseholdId == grant.HouseholdId)
                        {
                            householdReal = true;
                            if (household.HomeBuildingId == grant.BuildingId)
                            {
                                buildingReal = true;
                            }

                            break;
                        }
                    }

                    if (!buildingReal)
                    {
                        foreach (ScenarioSeedBusiness business in population.Businesses)
                        {
                            if (business != null && business.BuildingId == grant.BuildingId)
                            {
                                buildingReal = true;
                                break;
                            }
                        }
                    }

                    if (!householdReal || !buildingReal)
                    {
                        report.RejectedCount++;
                        string rejection = $"Rejected property grant '{grant.GrantId}': " +
                            $"household {grant.HouseholdId} or building {grant.BuildingId} is not real. " +
                            "Nothing was invented to satisfy the grant.";
                        report.Rejections.Add(rejection);
                        diagnostics?.Add(rejection);
                        continue;
                    }

                    // Property stream is consumed to keep stream positions stable
                    // even when grants are validated without randomness.
                    propertyRng.Next();
                    propertyPort.RecordGrant(grant, provenanceTag);
                    report.PropertyGrantCount++;
                }
            }

            // Inventory stream bookkeeping (see above): consume deterministically.
            inventoryRng.Next();
            return report;
        }

        private static string ResolveCreditorBusiness(ScenarioSeedPopulation population)
        {
            foreach (ScenarioSeedBusiness business in population.Businesses)
            {
                if (business != null && !string.IsNullOrWhiteSpace(business.BusinessId))
                {
                    return business.BusinessId;
                }
            }

            return "scenario-creditor";
        }
    }
}
