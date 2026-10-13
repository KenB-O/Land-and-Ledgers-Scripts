// Pass V P1-1/P1-5 (Worker 1): Unity-side production wiring for the scenario
// coordinator. NOT compiled in the Roslyn harness (see the exclusion in
// harness sources.txt): it binds the harness-clean coordinator core to the
// scene's real init services.
//
// Contents:
// - PopulationGeneratorPort: IScenarioGenerationPort backed by the existing
//   PopulationGenerator (town shell + PopulationGenerationSettings), then the
//   shared ScenarioEconomySeeder for coherent starting economy.
// - ScenarioHubResetPort: IScenarioWorldResetPort over the real managers,
//   using each authority's own clear-then-load idiom. No manager is bypassed.
// - ScenarioFileCampaignStore: IScenarioCampaignStore backed by JsonUtility +
//   Application.persistentDataPath (campaign metadata only).
//
// Production note: the SimulationSystemsHub owns HouseholdLedgers,
// Employments, and FinancialObligations; the bootstrapper constructs this
// wiring with those hub instances so the seeder writes through the ONE cash
// writer, ONE debt authority, and ONE employment registry.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// Converts an authored ScenarioAsset into a versioned ScenarioPreset for
    /// the coordinator. The asset remains the authoring authority; the preset
    /// is a deep copy used at generation time.
    /// </summary>
    public static class ScenarioAssetPresetBridge
    {
        public static ScenarioPreset ToPreset(ScenarioAsset asset, string presetId, string presetName, string presetVersion)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            List<string> problems = asset.Validate();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"ScenarioAsset '{asset.ScenarioId}' is invalid and cannot become a preset: {string.Join("; ", problems)}");
            }

            ScenarioPreset preset = asset.ToPreset(presetId, presetName, presetVersion);

            // Keep the scenario id consistent: the preset's starting world
            // carries the asset's scenario id.
            var diagnostics = new List<string>();
            if (!ScenarioPresetLibrary.Validate(preset, diagnostics))
            {
                throw new InvalidOperationException(
                    $"ScenarioAsset '{asset.ScenarioId}' converted to an invalid preset: {string.Join("; ", diagnostics)}");
            }

            return preset;
        }
    }

    /// <summary>
    /// Production generation port: runs the existing PopulationGenerator against
    /// the town shell, adapts the resulting PopulationState into a seed draft
    /// (read-only adaptation), then seeds the starting economy through the
    /// domain authorities with scenario provenance. Fixed businesses come from
    /// the preset; variable business formation stays with the NPC formation
    /// driver (out of scope for the coordinator).
    /// </summary>
    public sealed class PopulationGeneratorPort : IScenarioGenerationPort
    {
        private readonly TownWorldController townWorld;
        private readonly PopulationGenerationSettings settings;
        private readonly ScenarioEconomyAuthorityPorts economyPorts;
        private readonly IScenarioInventoryPort inventoryPort;
        private readonly IScenarioPropertyPort propertyPort;

        private int generationCalls;

        public int GenerationCalls => generationCalls;

        public PopulationGeneratorPort(
            TownWorldController townWorld,
            PopulationGenerationSettings settings,
            ScenarioEconomyAuthorityPorts economyPorts,
            IScenarioInventoryPort inventoryPort = null,
            IScenarioPropertyPort propertyPort = null)
        {
            this.townWorld = townWorld;
            this.settings = settings;
            this.economyPorts = economyPorts ?? throw new ArgumentNullException(nameof(economyPorts));
            this.inventoryPort = inventoryPort;
            this.propertyPort = propertyPort;
        }

        public ScenarioGenerationOutcome Generate(
            ScenarioPreset preset,
            int seed,
            int startDayIndex,
            GenerationRngStreams rng,
            List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (preset?.StartingWorld == null)
            {
                diagnostics.Add("PopulationGeneratorPort: preset or StartingWorld is missing; nothing generated.");
                return new ScenarioGenerationOutcome { Success = false, Summary = "missing preset" };
            }

            if (townWorld == null || settings == null)
            {
                diagnostics.Add("PopulationGeneratorPort: town shell or population settings is not wired; nothing generated.");
                return new ScenarioGenerationOutcome { Success = false, Summary = "town shell not wired" };
            }

            StartingWorldConfiguration world = preset.StartingWorld;
            ScenarioGenerationOutcome lastOutcome = null;
            GenerationRngStreams attemptRng = rng;

            for (int attempt = 0; attempt < GenerationRngStreams.MaxAttempts; attempt++)
            {
                generationCalls++;

                // The town seed drives PopulationGenerator's own stream; the
                // scenario seed offsets it deterministically per attempt.
                int scenarioSeedOffset = SeedMix(rng.Seed, attempt);
                int savedSeed = settings.seedOffset;
                try
                {
                    settings.seedOffset = savedSeed + scenarioSeedOffset;
                    PopulationGenerationResult result = PopulationGenerator.Generate(townWorld, settings);
                    if (result.HasErrors)
                    {
                        diagnostics.Add($"Attempt {attempt + 1}: PopulationGenerator reported errors: {result.Summary}");
                        foreach (string message in result.State.validationMessages)
                        {
                            diagnostics.Add($"Attempt {attempt + 1}: {message}");
                        }

                        attemptRng = attemptRng.NextAttempt();
                        continue;
                    }

                    ScenarioSeedPopulation draft = AdaptPopulationState(result.State, world, attemptRng);
                    string violation = ValidateDraft(draft, world);
                    if (violation != null)
                    {
                        diagnostics.Add($"Attempt {attempt + 1}: {violation} Retrying with repartitioned streams.");
                        attemptRng = attemptRng.NextAttempt();
                        continue;
                    }

                    ScenarioEconomySeeder.SeedReport seedReport = ScenarioEconomySeeder.Seed(
                        draft, preset, economyPorts, inventoryPort, propertyPort,
                        attemptRng, startDayIndex, diagnostics);

                    lastOutcome = new ScenarioGenerationOutcome
                    {
                        Success = true,
                        WorldFingerprint = ScenarioWorldBuilder.ComputeWorldFingerprint(
                            draft, seedReport, preset, rng.Seed, startDayIndex),
                        ResidentCount = draft.ResidentCount,
                        HouseholdCount = draft.Households.Count,
                        BusinessCount = draft.Businesses.Count,
                        AttemptUsed = attempt,
                        Summary = result.Summary,
                    };
                    lastOutcome.ProvenanceTags.Add(ScenarioCampaignRecord.BuildProvenanceTag(
                        preset.PresetId, world.DefinitionVersion, rng.Seed));
                    return lastOutcome;
                }
                finally
                {
                    settings.seedOffset = savedSeed;
                }
            }

            diagnostics.Add($"PopulationGeneratorPort: preset '{preset.PresetId}' seed {rng.Seed} failed all " +
                $"{GenerationRngStreams.MaxAttempts} bounded attempts. Nothing was invented to force validity.");
            return lastOutcome ?? new ScenarioGenerationOutcome { Success = false, Summary = "all attempts failed" };
        }

        private static int SeedMix(int seed, int attempt)
        {
            unchecked
            {
                return (int)(((long)seed * 31L + (long)attempt * 0x9E3779B9L) & 0x7FFFFFFF);
            }
        }

        /// <summary>
        /// Read-only adaptation: PopulationState stays owned by the population
        /// domain; the draft only carries the ids and values the seeder needs.
        /// </summary>
        private static ScenarioSeedPopulation AdaptPopulationState(
            PopulationState state,
            StartingWorldConfiguration world,
            GenerationRngStreams rng)
        {
            var draft = new ScenarioSeedPopulation();
            System.Random bizRng = rng.Stream(GenerationConcern.BusinessFormation);

            foreach (HouseholdState household in state.households)
            {
                if (household == null)
                {
                    continue;
                }

                var seedHousehold = new ScenarioSeedHousehold
                {
                    HouseholdId = household.id,
                    Surname = household.surname ?? string.Empty,
                    HomeBuildingId = household.homeBuildingId,
                };

                foreach (int memberId in household.memberIds)
                {
                    PersonState person = state.GetPerson(memberId);
                    if (person == null)
                    {
                        continue;
                    }

                    seedHousehold.Members.Add(new ScenarioSeedPerson
                    {
                        PersonId = person.id,
                        HouseholdId = household.id,
                        FirstName = person.firstName ?? string.Empty,
                        LastName = person.lastName ?? string.Empty,
                        Age = person.age,
                        IsWorkingAge = person.age >= 16 && person.age <= 64,
                        ProfessionId = person.professionId ?? string.Empty,
                        ProfessionName = person.professionName ?? string.Empty,
                        WeeklyWageCents = Math.Max(0, person.wage.weeklyWage * 100),
                    });
                }

                draft.Households.Add(seedHousehold);
            }

            // The player household is the first generated household when the
            // scenario declares founders; founder resolution stays with the
            // ScenarioDirector's existing BootstrapPlayerHousehold path.
            if (draft.Households.Count > 0 && world.PlayerStart != null)
            {
                draft.Households[0].IsPlayerHousehold = true;
                draft.PlayerHouseholdId = draft.Households[0].HouseholdId;
            }

            int buildingId = 2000;
            foreach (ScenarioFixedBusiness fixedBusiness in world.FixedBusinesses)
            {
                if (fixedBusiness == null || string.IsNullOrWhiteSpace(fixedBusiness.BusinessId))
                {
                    continue;
                }

                draft.Businesses.Add(new ScenarioSeedBusiness
                {
                    BusinessId = fixedBusiness.BusinessId,
                    DisplayName = string.IsNullOrWhiteSpace(fixedBusiness.DisplayName)
                        ? fixedBusiness.BusinessId : fixedBusiness.DisplayName,
                    BusinessType = string.IsNullOrWhiteSpace(fixedBusiness.BusinessType)
                        ? "general" : fixedBusiness.BusinessType,
                    CapitalCents = Math.Max(0, fixedBusiness.CapitalCents),
                    OwnerKind = string.IsNullOrWhiteSpace(fixedBusiness.OwnerKind)
                        ? "npc" : fixedBusiness.OwnerKind,
                    BuildingId = buildingId++,
                });
            }

            _ = bizRng;
            return draft;
        }

        private static string ValidateDraft(ScenarioSeedPopulation draft, StartingWorldConfiguration world)
        {
            if (draft.Households.Count == 0)
            {
                return "PopulationGenerator produced no households.";
            }

            int residents = draft.ResidentCount;
            if (residents < world.MinResidents || residents > world.MaxResidents)
            {
                return $"PopulationGenerator produced {residents} residents, outside the authored range " +
                    $"[{world.MinResidents},{world.MaxResidents}].";
            }

            return null;
        }
    }

    /// <summary>
    /// Production reset port: purges run-scoped state through each owning
    /// authority's own clear-then-load idiom before a New Game / Restart /
    /// Replay. Categories with no wired service are reported loudly in the
    /// diagnostics rather than silently skipped.
    /// </summary>
    public sealed class ScenarioHubResetPort : IScenarioWorldResetPort
    {
        private readonly PopulationManager populationManager;
        private readonly FinancialObligationAuthority obligations;
        private readonly EmploymentRelationshipRegistry employments;
        private readonly HouseholdLedgerRegistry ledgers;
        private readonly EquipmentReservationService reservations;
        private readonly ScenarioService scenarioService;
        private readonly LandLedgers.Economy.PlayerPortfolioManager portfolio;
        private readonly LandLedgers.Economy.Financing.PlayerDebtManager debtManager;

        private readonly List<string> clearedCategories = new List<string>();

        public IReadOnlyList<string> ClearedCategories => clearedCategories;

        public ScenarioHubResetPort(
            PopulationManager populationManager = null,
            FinancialObligationAuthority obligations = null,
            EmploymentRelationshipRegistry employments = null,
            HouseholdLedgerRegistry ledgers = null,
            EquipmentReservationService reservations = null,
            ScenarioService scenarioService = null,
            LandLedgers.Economy.PlayerPortfolioManager portfolio = null,
            LandLedgers.Economy.Financing.PlayerDebtManager debtManager = null)
        {
            this.populationManager = populationManager;
            this.obligations = obligations;
            this.employments = employments;
            this.ledgers = ledgers;
            this.reservations = reservations;
            this.scenarioService = scenarioService;
            this.portfolio = portfolio;
            this.debtManager = debtManager;
        }

        public void ResetForNewGame(List<string> diagnostics)
        {
            clearedCategories.Clear();

            ClearOrNote(populationManager != null, "household-memberships",
                () => populationManager.ClearPopulationSnapshot(), diagnostics);
            ClearOrNote(employments != null, "employment",
                () => employments.LoadFromSaveDto(new EmploymentRegistrySaveDto()), diagnostics);
            ClearOrNote(scenarioService != null, "scenario-objectives",
                () => scenarioService.EndScenario("new game reset"), diagnostics);
            ClearOrNote(obligations != null, "banking-state",
                () => obligations.LoadFromSaveDto(new FinancialObligationSaveDto()), diagnostics);
            ClearOrNote(ledgers != null, "banking-state",
                () => ledgers.ClearForNewGame("new game reset"), diagnostics);
            ClearOrNote(portfolio != null, "player-ownership",
                () => portfolio.LoadFromSaveDto(new PlayerPortfolioSaveDto()), diagnostics);
            ClearOrNote(debtManager != null, "banking-state",
                () => debtManager.LoadFromSaveDto(new PlayerDebtSaveDto()), diagnostics);
            ClearOrNote(reservations != null, "stale-reservations",
                () => reservations.LoadFromSaveDto(new EquipmentReservationAuthoritySaveDto()), diagnostics);

            // "generated-businesses" and "manager-decisions" are cleared by the
            // business runtime's own new-game path via the bootstrapper; when
            // that path is not wired, say so loudly instead of pretending.
            diagnostics?.Add(clearedCategories.Contains("generated-businesses")
                ? "generated-businesses cleared."
                : "NOTE: generated-businesses and manager-decisions reset is owned by the " +
                  "business runtime bootstrapper path; wire it before in-scene Restart is offered to players.");
        }

        private void ClearOrNote(bool wired, string category, Action clear, List<string> diagnostics)
        {
            if (!wired)
            {
                diagnostics?.Add($"ResetForNewGame: no service wired for '{category}'; left untouched (loud, not silent).");
                return;
            }

            clear();
            if (!clearedCategories.Contains(category))
            {
                clearedCategories.Add(category);
            }
        }
    }

    /// <summary>
    /// Production campaign store: persists the coordinator's campaign record
    /// (metadata only) as JSON under Application.persistentDataPath.
    /// </summary>
    public sealed class ScenarioFileCampaignStore : IScenarioCampaignStore
    {
        private static string PathFor(string slotId)
        {
            string safe = string.IsNullOrWhiteSpace(slotId) ? "default" : slotId;
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid.ToString(), "_");
            }

            return System.IO.Path.Combine(Application.persistentDataPath, $"scenario-campaign-{safe}.json");
        }

        public bool TryLoad(string slotId, out ScenarioCampaignRecord record)
        {
            record = null;
            try
            {
                string path = PathFor(slotId);
                if (!System.IO.File.Exists(path))
                {
                    return false;
                }

                record = JsonUtility.FromJson<ScenarioCampaignRecord>(System.IO.File.ReadAllText(path));
                return record != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScenarioFileCampaignStore] Load failed for slot '{slotId}': {ex.Message}");
                return false;
            }
        }

        public void Save(string slotId, ScenarioCampaignRecord record)
        {
            if (record == null)
            {
                return;
            }

            try
            {
                System.IO.File.WriteAllText(PathFor(slotId), JsonUtility.ToJson(record, prettyPrint: true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScenarioFileCampaignStore] Save failed for slot '{slotId}': {ex.Message}");
            }
        }

        public void Delete(string slotId)
        {
            try
            {
                string path = PathFor(slotId);
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScenarioFileCampaignStore] Delete failed for slot '{slotId}': {ex.Message}");
            }
        }
    }
}
